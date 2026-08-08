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
        Training,
        Disabled
    }

    public enum VehicleEvasionState
    {
        Ready,
        Active,
        Recovery,
        Cooldown
    }

    [Serializable]
    public struct VehicleEvasionSnapshot
    {
        public VehicleEvasionState state;
        public float actionTimeRemaining;
        public float cooldownTimeRemaining;
        public Vector3 worldDirection;
        public float requestedSpeedDelta;
        public float actualSpeedDelta;
        public float rollProgress;
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
        public Vector3 positiveForce;
        public Vector3 negativeForce;
        public Vector3 positiveAcceleration;
        public Vector3 negativeAcceleration;
        public Vector3 angularAcceleration;
        public Vector3 estimatedAngularSpeed;
        public float stallSpeed;
        public float zeroToFiftyTime;
        public float brakingTime;
        public float stabilizerUsage;
        public int activeWheels;
        public int activeAirMovers;
        public Vector3 exposedArea;
        public Vector3 dragArea;
        public float wingArea;
        public float currentLift;
        public float currentDrag;
        public Vector3 liftCenterLocal;
        public Vector3 dragCenterLocal;
        public Vector3 aerodynamicTorqueLocal;
        public Vector3 meanWindVelocity;
        public Vector3 gustVelocity;
        public float airDensity;
        public float ambientPressure;
        public float angleOfAttack;
        public float sideslipAngle;
        public bool inertiaTriangleValid;
        public bool inertiaFallbackUsed;
        public int aeroPanelCount;
        public int stalledPanelCount;
        public float separation;
        public float leftWingSeparation;
        public float rightWingSeparation;
        public float envelopeInputScale;
        public float staticMargin;
        public float trimUsage;
        public float averageGroundEffect;
        public Vector3 controlSurfaceAuthority;
        public Vector3 netForceWorld;
        public Vector3 netTorqueWorld;
        public Vector3 predictedLinearAccelerationWorld;
        public Vector3 predictedAngularAccelerationWorld;
        public Vector3 requestedControlForceWorld;
        public Vector3 requestedControlTorqueWorld;
        public Vector3 actualControlForceWorld;
        public Vector3 actualControlTorqueWorld;
        public Vector3 controlForceResidualWorld;
        public Vector3 controlTorqueResidualWorld;
        public float controlClosureRatio;
        public bool controlRequestSatisfied;
        public bool wholeBodyValid;
        public string physicsAudit;
        public string status;

        public string BuildSummary()
        {
            return
                $"RC3 质量 {totalMass:0} kg\n" +
                $"质心 {centerOfMassLocal.x:0.0}, " +
                $"{centerOfMassLocal.y:0.0}, {centerOfMassLocal.z:0.0}\n" +
                $"主惯量 {inertia.x:0}, {inertia.y:0}, {inertia.z:0}\n" +
                $"阻力面积 {dragArea.x:0.0}, {dragArea.y:0.0}, {dragArea.z:0.0}\n" +
                $"翼面积 {wingArea:0.0}  失速 {stallSpeed:0.0} m/s\n" +
                $"地速 {groundTopSpeed:0}  空速 {airTopSpeed:0} m/s\n" +
                $"升重比 {hoverRatio:0.00}  轮胎 {activeWheels}\n" +
                $"移动件 {activeAirMovers}  {status}";
        }
    }

    [Serializable]
    public struct VehicleActuatorDiagnostic
    {
        public string runtimeId;
        public string sourceId;
        public Vector3 localPosition;
        public Vector3 localForceDirection;
        public float configuredMaximumForce;
        public float effectiveMaximumForce;
        public Vector3 maximumTorqueLocal;
        public float responseTime;
        public float targetThrottle;
        public float actualThrottle;
        public bool atmosphereOnly;
        public bool propeller;
    }

    [Serializable]
    public struct RobocraftControlFrame
    {
        public Vector2 move;
        public float vertical;
        public float roll;
        public bool braking;
        public bool boost;
        public bool freeLook;
        public Vector3 aimForwardWorld;
        public bool hasAimOverride;
        public bool evadeRequested;
        public Vector2 evadeDirection;
    }

    public struct TrainingFlightAssistDemand
    {
        public Vector3 holdPosition;
        public Vector3 controlAccelerationWorld;
        public Vector3 intentAccelerationWorld;
        public Vector3 driftCancellationAccelerationWorld;
        public Vector3 stopAccelerationWorld;
        public Vector3 positionHoldAccelerationWorld;
        public Vector3 targetVelocityWorld;
        public float targetSpeed;
    }

    /// <summary>
    /// The persisted Training enum now represents the player-facing arcade
    /// mode. It converts a full 3D movement intent into a responsive velocity
    /// target and switches to position hold as soon as the player releases the
    /// controls. The returned acceleration remains a physical request: RC3's
    /// normal actuator allocator still limits it to installed/core authority.
    /// </summary>
    public static class TrainingFlightAssist
    {
        public const float VelocityResponseSeconds = 0.16f;
        public const float IntentAuthorityFraction = 0.75f;
        public const float StopVelocityGain = 10f;
        public const float BasePlanarAssistForce = 2500f;
        public const float DriveVectoringFraction = 0.72f;
        public const float MaximumVectoringAcceleration = 14f;
        const float HoldPositionGain = 5.00f;
        const float HoldVelocityGain = 4.60f;

        public static bool ShouldPrioritizePlayerIntent(
            VehicleCoreAssistMode assistMode,
            bool controlsEnabled,
            bool injectedControlActive)
        {
            return assistMode == VehicleCoreAssistMode.Training &&
                   controlsEnabled &&
                   !injectedControlActive;
        }

        /// <summary>
        /// Arcade mode treats part of the installed planar drive as bounded
        /// thrust vectoring while the hull is still turning toward the mouse
        /// aim.  It keeps W/A responsive in the camera/reticle frame without
        /// granting a fixed force that ignores propulsion damage.
        /// </summary>
        public static float CalculatePlanarAssistForce(
            float vehicleMass,
            float strongestInstalledPlanarForce)
        {
            float vectoringForce = Mathf.Min(
                Mathf.Max(0f, strongestInstalledPlanarForce) *
                DriveVectoringFraction,
                Mathf.Max(1f, vehicleMass) *
                MaximumVectoringAcceleration);
            return BasePlanarAssistForce + vectoringForce;
        }

        /// <summary>
        /// Resolves arcade speed authority from the player's semantic axes,
        /// not from the hull's unfinished rotation.  W therefore continues to
        /// use forward-drive authority while the ship turns toward the aim;
        /// A/D and Space/Ctrl retain their own installed-axis authority.
        /// </summary>
        public static float CalculateCommandAcceleration(
            Vector3 localCommandAxes,
            Vector3 positiveAxisAcceleration,
            Vector3 negativeAxisAcceleration)
        {
            Vector3 command = Vector3.ClampMagnitude(
                localCommandAxes,
                1f);
            Vector3 weights = new Vector3(
                Mathf.Abs(command.x),
                Mathf.Abs(command.y),
                Mathf.Abs(command.z));
            float totalWeight = weights.x + weights.y + weights.z;
            if (totalWeight <= 0.0001f)
                return 0f;

            float x = command.x >= 0f
                ? positiveAxisAcceleration.x
                : negativeAxisAcceleration.x;
            float y = command.y >= 0f
                ? positiveAxisAcceleration.y
                : negativeAxisAcceleration.y;
            float z = command.z >= 0f
                ? positiveAxisAcceleration.z
                : negativeAxisAcceleration.z;
            return Mathf.Max(
                0f,
                (weights.x * Mathf.Max(0f, x) +
                 weights.y * Mathf.Max(0f, y) +
                 weights.z * Mathf.Max(0f, z)) /
                totalWeight);
        }

        public static float CalculateTargetSpeed(
            float availableAcceleration,
            bool boost,
            ArcadeFlightTuningProfile tuning = null)
        {
            float baseSpeed = tuning != null
                ? tuning.baseTargetSpeed
                : 12f;
            float minimumSpeed = tuning != null
                ? tuning.minimumTargetSpeed
                : 12f;
            float maximumSpeed = tuning != null
                ? tuning.maximumTargetSpeed
                : 65f;
            float accelerationToSpeed = tuning != null
                ? tuning.accelerationToSpeed
                : 3.5f;
            float boostMultiplier = tuning != null
                ? tuning.boostSpeedMultiplier
                : 1.60f;
            float speed = Mathf.Clamp(
                baseSpeed +
                Mathf.Max(0f, availableAcceleration) *
                accelerationToSpeed,
                minimumSpeed,
                maximumSpeed);
            return speed * (boost ? boostMultiplier : 1f);
        }

        public static bool HasMovementIntent(
            Vector3 movementInputWorld,
            ArcadeFlightTuningProfile tuning = null)
        {
            float deadzone = tuning != null
                ? tuning.movementDeadzone
                : 0.01f;
            return movementInputWorld.sqrMagnitude >
                   deadzone * deadzone;
        }

        public static TrainingFlightAssistDemand CalculateDemand(
            Vector3 position,
            Vector3 velocity,
            Vector3 holdPosition,
            bool holdInitialized,
            Vector3 movementInputWorld,
            float targetSpeed,
            ArcadeFlightTuningProfile tuning = null)
        {
            if (!holdInitialized)
                holdPosition = position;

            Vector3 movementInput = Vector3.ClampMagnitude(
                movementInputWorld,
                1f);
            if (!HasMovementIntent(movementInput, tuning))
                movementInput = Vector3.zero;
            Vector3 targetVelocity = Vector3.zero;
            Vector3 intentAcceleration = Vector3.zero;
            Vector3 driftCancellationAcceleration = Vector3.zero;
            Vector3 stopAcceleration = Vector3.zero;
            Vector3 positionHoldAcceleration = Vector3.zero;
            Vector3 controlAcceleration;
            if (movementInput.sqrMagnitude > 0f)
            {
                holdPosition = position;
                Vector3 intentDirection = movementInput.normalized;
                float inputExponent = tuning != null
                    ? tuning.inputResponseExponent
                    : 1f;
                float targetSpeedAlongIntent =
                    Mathf.Max(0f, targetSpeed) *
                    Mathf.Pow(
                        movementInput.magnitude,
                        inputExponent);
                float currentSpeedAlongIntent = Vector3.Dot(
                    velocity,
                    intentDirection);
                Vector3 perpendicularDrift = velocity -
                    intentDirection * currentSpeedAlongIntent;
                targetVelocity = intentDirection *
                    targetSpeedAlongIntent;
                float intentResponse = tuning != null
                    ? tuning.intentResponseSeconds
                    : VelocityResponseSeconds;
                float driftResponse = tuning != null
                    ? tuning.driftResponseSeconds
                    : VelocityResponseSeconds;
                intentAcceleration = intentDirection *
                    ((targetSpeedAlongIntent - currentSpeedAlongIntent) /
                     Mathf.Max(0.01f, intentResponse));
                driftCancellationAcceleration =
                    -perpendicularDrift /
                    Mathf.Max(0.01f, driftResponse);
                controlAcceleration = intentAcceleration +
                    driftCancellationAcceleration;
            }
            else
            {
                Vector3 positionError = holdPosition - position;
                float stopGain = tuning != null
                    ? tuning.stopVelocityGain
                    : StopVelocityGain;
                float positionGain = tuning != null
                    ? tuning.positionHoldGain
                    : HoldPositionGain;
                stopAcceleration = -velocity * stopGain;
                positionHoldAcceleration =
                    positionError * positionGain;
                // Keep the legacy combined request unchanged for injected
                // AI/autopilot callers. Direct player arcade control consumes
                // the stronger stop and hold requests separately.
                controlAcceleration =
                    positionError * HoldPositionGain -
                    velocity * HoldVelocityGain;
            }

            return new TrainingFlightAssistDemand
            {
                holdPosition = holdPosition,
                controlAccelerationWorld = controlAcceleration,
                intentAccelerationWorld = intentAcceleration,
                driftCancellationAccelerationWorld =
                    driftCancellationAcceleration,
                stopAccelerationWorld = stopAcceleration,
                positionHoldAccelerationWorld =
                    positionHoldAcceleration,
                targetVelocityWorld = targetVelocity,
                targetSpeed = targetVelocity.magnitude
            };
        }
    }

    public interface IRobocraftPilotAimSource
    {
        bool TryGetPilotAim(out Vector3 worldForward);
    }

    [DisallowMultipleComponent]
    public sealed class RobocraftMotionCoordinator :
        MonoBehaviour,
        IVehicleExternalForceSink,
        IVehicleCurrentPhysicsStepImpulseSink
    {
        const float CoreDampingTorque = 1200f;
        const float TrainingUpForce = 13000f;
        const float TrainingDownForce = 3000f;
        const float TrainingTorque = 1200f;
        const float CruiseThrottle = 0.72f;
        const float EvasionSpeedDelta = 26f;
        const float EvasionRollSpeed = 18f;
        const float EvasionRollDuration =
            2f * Mathf.PI / EvasionRollSpeed;
        const float EvasionRecoveryDuration = 0.12f;
        const float EvasionActionDuration =
            EvasionRollDuration + EvasionRecoveryDuration;
        const float EvasionCooldownDuration = 1.5f;


        sealed class AirMover
        {
            public NeoXBehaviorModule module;
            public NeoXThrusterExhaustVfx vfx;
            public string runtimeId;
            public Vector3 localPosition;
            public Vector3 localDirection;
            public float maximumForce;
            public float responseTime;
            public float softSpeed;
            public bool atmosphereOnly;
            public bool propeller;
            public float diskRadius;
            public int rotationSign;
            public float intakeEfficiency;
            public float targetThrottle;
            public float actualThrottle;
        }

        Rigidbody body;
        ShipAssembly assembly;
        GridAssemblyModel model;
        GridAssemblyPresenter presenter;
        readonly VehicleForceLedger ledger = new VehicleForceLedger();
        readonly VirtualRcs24Allocator rcs24 =
            new VirtualRcs24Allocator();
        readonly List<AirMover> airMovers = new List<AirMover>();
        readonly VehicleGroundMobility groundMobility =
            new VehicleGroundMobility();
        readonly VehiclePhysicsRc3State rc3Physics =
            new VehiclePhysicsRc3State();
        readonly VehicleAirflowField airflowField =
            new VehicleAirflowField();
        readonly List<PropellerWashSource> propellerWashes =
            new List<PropellerWashSource>();
        VehicleStructureGraph structureGraph;
        IPlanetEnvironmentProvider environmentProvider;
        IRobocraftPilotAimSource pilotAimSource;
        ArcadeFlightTuningProfile arcadeFlightTuning;
        bool arcadeFlightTuningResolved;
        PlanetEnvironmentSample environmentSample =
            PlanetEnvironmentSample.EarthLike(
                Vector3.down * 9.81f,
                0f);
        VehicleCoreAssistMode coreAssistMode =
            VehicleCoreAssistMode.Standard;
        Vector3 gravity = Vector3.down * 9.81f;
        float airDensity = 1.225f;
        float altitude;
        bool active;
        bool controlsEnabled = true;
        bool injectedControlActive;
        RobocraftControlFrame injectedControlFrame;
        float actuatorForceMultiplier = 1f;
        float massGeometryScale = 1f;
        float trainingPlanarAssistForce =
            TrainingFlightAssist.BasePlanarAssistForce;
        float trainingUpAssistForce = TrainingUpForce;
        float trainingDownAssistForce = TrainingDownForce;
        bool trainingCoreAuthorityOverride;
        bool damageDisabled;
        bool rebuildPending;
        Vector3 heldAimForward = Vector3.forward;
        Vector3 hoverPosition;
        bool hoverHeld;
        Vector3 trainingHoldPosition;
        bool trainingHoldInitialized;
        float[] moverForceScales = Array.Empty<float>();
        bool moverForceScalesValid;
        Vector3 bodyCdArea = Vector3.one * 0.7f;
        float totalWingArea;
        Vector3 trimTorqueLocal;
        Vector3 plannedAeroTorqueLocal;
        bool pilotRotationActive;
        Vector3 requestedControlForceWorld;
        Vector3 requestedControlTorqueWorld;
        Vector3 actualControlForceWorld;
        Vector3 actualControlTorqueWorld;
        Vector3 queuedExternalImpulseWorld;
        Vector3 queuedExternalAngularImpulseWorld;
        bool physicsStepRunning;
        bool physicsStepCompleted;
        double activePhysicsStepTime;
        double completedPhysicsStepTime;
        RobocraftTelemetry telemetry;
        bool physicsOwnershipExclusive;
        VehicleEvasionState evasionState =
            VehicleEvasionState.Ready;
        VehicleEvasionSnapshot evasionSnapshot;
        VehicleEvasionPresentation evasionPresentation;
        Vector2 pendingEvasionDirection;
        bool pendingEvasionRequest;
        bool diagnosticFlight;
        Vector3 evasionWorldDirection;
        Vector3 evasionRollAxisWorld;
        Quaternion evasionStartRotation;
        Vector3 evasionBaselineAngularVelocity;
        Vector3 velocityBeforeEvasionImpulse;
        float evasionBaselineRollSpeed;
        float evasionRollSign;
        float evasionAccumulatedRollRadians;
        float evasionRecoveryElapsed;
        float evasionActionElapsed;
        float evasionCooldownRemaining;
        float evasionActualSpeedDelta;
        bool measureEvasionVelocity;

        public bool IsActive => active && !damageDisabled;
        public bool OwnsPhysics => active;
        public bool ControlsEnabled
        {
            get => controlsEnabled;
            set
            {
                controlsEnabled = value;
                if (!value)
                {
                    pendingEvasionRequest = false;
                    ZeroAirThrottle();
                }
            }
        }
        public VehicleCoreAssistMode CoreAssistMode => coreAssistMode;
        public ArcadeFlightTuningProfile RuntimeArcadeFlightTuning =>
            PlayerArcadeTuning;
        public float ActuatorForceMultiplier => actuatorForceMultiplier;
        public float MassGeometryScale => massGeometryScale;
        public float TrainingPlanarAssistForce =>
            trainingPlanarAssistForce;
        public float TrainingUpAssistForce => trainingUpAssistForce;
        public float TrainingDownAssistForce => trainingDownAssistForce;
        public RobocraftTelemetry Telemetry => telemetry;
        public VehicleEvasionSnapshot EvasionSnapshot =>
            evasionSnapshot;
        public DirectionalAuthority24 Authority24 => rcs24.Authority;
        public VehiclePhysicsSnapshot PhysicsSnapshot => rc3Physics.Snapshot;
        public IReadOnlyList<ModularWheelRuntime> Wheels =>
            groundMobility.Wheels;
        public float GroundSupportRatio => groundMobility.SupportRatio;
        public float GroundControlWeight =>
            groundMobility.GroundControlWeight;
        public IReadOnlyList<ModuleAeroSurface> AeroSurfaces =>
            rc3Physics.AeroSurfaces;

        public VehicleActuatorDiagnostic[] CaptureActuatorDiagnostics()
        {
            var result = new VehicleActuatorDiagnostic[airMovers.Count];
            Vector3 centerOfMass = body != null
                ? body.centerOfMass
                : rc3Physics.MassProperties.centerOfMassLocal;
            for (int index = 0; index < airMovers.Count; index++)
            {
                AirMover mover = airMovers[index];
                Vector3 direction = mover.localDirection.sqrMagnitude > 0.0001f
                    ? mover.localDirection.normalized
                    : Vector3.zero;
                float effectiveForce =
                    mover.maximumForce * EffectiveThrustFactor(mover);
                Vector3 force = direction * effectiveForce;
                result[index] = new VehicleActuatorDiagnostic
                {
                    runtimeId = mover.runtimeId ?? string.Empty,
                    sourceId = mover.module != null
                        ? mover.module.SourceId
                        : string.Empty,
                    localPosition = mover.localPosition,
                    localForceDirection = direction,
                    configuredMaximumForce = mover.maximumForce,
                    effectiveMaximumForce = effectiveForce,
                    maximumTorqueLocal = Vector3.Cross(
                        mover.localPosition - centerOfMass,
                        force),
                    responseTime = mover.responseTime,
                    targetThrottle = mover.targetThrottle,
                    actualThrottle = mover.actualThrottle,
                    atmosphereOnly = mover.atmosphereOnly,
                    propeller = mover.propeller
                };
            }
            return result;
        }

        public void Configure(Rigidbody target, ShipAssembly shipAssembly)
        {
            body = target;
            assembly = shipAssembly;
            ResolvePilotAimSource();
            ModularAssemblyLabController lab =
                FindObjectOfType<ModularAssemblyLabController>();
            BindModel(lab != null ? lab.Model : null);
            presenter = GetComponent<GridAssemblyPresenter>();
            if (presenter != null)
            {
                presenter.Rebuilt -= HandlePresenterRebuilt;
                presenter.Rebuilt += HandlePresenterRebuilt;
            }
            BindStructureGraph(GetComponent<VehicleStructureGraph>());
            EnsureEvasionPresentation();
            Rebuild();
            enabled = true;
        }

public void ConfigureExplicit(
            Rigidbody target,
            ShipAssembly shipAssembly,
            GridAssemblyModel assemblyModel,
            GridAssemblyPresenter assemblyPresenter)
        {
            if (presenter != null)
                presenter.Rebuilt -= HandlePresenterRebuilt;
            body = target;
            assembly = shipAssembly;
            ResolvePilotAimSource();
            BindModel(assemblyModel);
            presenter = assemblyPresenter;
            if (presenter != null)
            {
                presenter.Rebuilt -= HandlePresenterRebuilt;
                presenter.Rebuilt += HandlePresenterRebuilt;
            }
            BindStructureGraph(GetComponent<VehicleStructureGraph>());
            EnsureEvasionPresentation();
            Rebuild();
            enabled = true;
        }


        public void BeginFlight()
        {
            TryBeginFlight(false, out _);
        }

        public bool TryBeginFlight(
            bool preserveExistingMotion,
            out string message)
        {
            if (body == null)
            {
                message = "RC3 Rigidbody 未配置。";
                return false;
            }
            // Runtime integrations can finish wiring their pilot source after
            // this coordinator is configured. Resolve once more at the actual
            // flight boundary so manual aim cannot be lost during assembly.
            ResolvePilotAimSource();
            diagnosticFlight = false;
            ResetEvasion();
            ResetTrainingHold();
            ClearQueuedExternalImpulses();
            rc3Physics.ResetAerodynamicsRuntime();
            trimTorqueLocal = Vector3.zero;
            plannedAeroTorqueLocal = Vector3.zero;
            Rebuild();
            PhysicsConflictAuditResult audit =
                PhysicsConflictAudit.AuditAndResolve(gameObject, body, this);
            physicsOwnershipExclusive = !audit.fatal;
            telemetry.physicsAudit = audit.message;
            if (audit.fatal)
            {
                active = false;
                message = audit.message;
                Debug.LogError(audit.message, this);
                return false;
            }
            damageDisabled = false;
            body.solverIterations = 8;
            body.solverVelocityIterations = 3;
            body.maxAngularVelocity = 12f;
            body.maxDepenetrationVelocity = 5f;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            if (!body.isKinematic && !preserveExistingMotion)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            groundMobility.SetFlightMode(true);
            heldAimForward = CameraForward();
            BindStructureGraph(GetComponent<VehicleStructureGraph>());
            active = true;
            EnsureEvasionPresentation();
            evasionPresentation?.SetFlightActive(true);
            message = audit.message;
            return true;
        }

        public void BeginDiagnosticFlight()
        {
            if (body == null)
                return;
            diagnosticFlight = true;
            ResetEvasion();
            ResetTrainingHold();
            ClearQueuedExternalImpulses();
            rc3Physics.ResetAerodynamicsRuntime();
            trimTorqueLocal = Vector3.zero;
            plannedAeroTorqueLocal = Vector3.zero;
            Rebuild();
            physicsOwnershipExclusive = true;
            telemetry.physicsAudit =
                "隔离 PhysicsScene 自动盘旋测试；未运行全局物理冲突修复。";
            damageDisabled = false;
            body.solverIterations = 8;
            body.solverVelocityIterations = 3;
            body.maxAngularVelocity = 12f;
            body.maxDepenetrationVelocity = 5f;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.None;
            groundMobility.SetFlightMode(true);
            heldAimForward = transform.forward;
            BindStructureGraph(GetComponent<VehicleStructureGraph>());
            active = true;
        }

        public void EndDiagnosticFlight()
        {
            EndFlight();
        }


        public void EndFlight()
        {
            active = false;
            injectedControlActive = false;
            injectedControlFrame = default;
            damageDisabled = false;
            diagnosticFlight = false;
            ResetEvasion();
            ResetTrainingHold();
            evasionPresentation?.SetFlightActive(false);
            ClearQueuedExternalImpulses();
            rc3Physics.ResetAerodynamicsRuntime();
            trimTorqueLocal = Vector3.zero;
            plannedAeroTorqueLocal = Vector3.zero;
            ZeroAirThrottle();
            groundMobility.SetFlightMode(false);
        }

        public void SetCoreAssistMode(VehicleCoreAssistMode value)
        {
            if (!Enum.IsDefined(typeof(VehicleCoreAssistMode), value))
                value = VehicleCoreAssistMode.Standard;
            coreAssistMode = value;
            ResetTrainingHold();
            model?.SetCoreAssistMode(value);
            RebuildRcs24Allocator();
            RefreshTelemetry();
        }

        /// <summary>
        /// Supplies vehicle-specific pseudo-physical emergency authority for
        /// Training assist. Standard flight remains entirely module-driven.
        /// This is used by oversized modular Bosses so losing thrusters makes
        /// them slower and less stable without ever removing basic flight.
        /// </summary>
        public void ConfigureTrainingCoreAuthority(
            float upwardForce,
            float downwardForce,
            float planarForce)
        {
            trainingCoreAuthorityOverride = true;
            trainingUpAssistForce = SanitizeTrainingForce(
                upwardForce,
                TrainingUpForce);
            trainingDownAssistForce = SanitizeTrainingForce(
                downwardForce,
                TrainingDownForce);
            trainingPlanarAssistForce = SanitizeTrainingForce(
                planarForce,
                TrainingFlightAssist.BasePlanarAssistForce);
            RebuildRcs24Allocator();
            RefreshTelemetry();
        }

        static float SanitizeTrainingForce(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return fallback;
            return Mathf.Clamp(value, 1f, 100000000f);
        }

        /// <summary>
        /// Applies a vehicle-level calibration to installed movement modules.
        /// The default is one. Damage still removes individual actuators, so
        /// asymmetric thrust and lost authority remain fully represented.
        /// </summary>
        public void SetActuatorForceMultiplier(float value)
        {
            float sanitized = Mathf.Clamp(
                float.IsNaN(value) || float.IsInfinity(value) ? 1f : value,
                0.01f,
                64f);
            if (Mathf.Approximately(actuatorForceMultiplier, sanitized))
                return;
            actuatorForceMultiplier = sanitized;
            Rebuild();
        }

        /// <summary>
        /// Scales module centers and dimensions for rigidbody mass geometry
        /// without changing module mass. This keeps inertia consistent with
        /// vehicles whose individual module colliders are visually enlarged.
        /// </summary>
        public void SetMassGeometryScale(float value)
        {
            float sanitized = Mathf.Clamp(
                float.IsNaN(value) || float.IsInfinity(value) ? 1f : value,
                0.1f,
                20f);
            if (Mathf.Approximately(massGeometryScale, sanitized))
                return;
            massGeometryScale = sanitized;
            Rebuild();
        }

        public void SetPlanetEnvironment(
            Vector3 acceleration,
            float density,
            float heightAboveGround)
        {
            gravity = acceleration;
            airDensity = Mathf.Max(0f, density);
            altitude = Mathf.Max(0f, heightAboveGround);
            environmentSample = new PlanetEnvironmentSample
            {
                gravityAcceleration = gravity,
                altitude = altitude,
                airDensity = airDensity,
                ambientPressure = 101325f *
                    Mathf.Clamp01(airDensity / 1.225f),
                meanWindVelocity = Vector3.zero,
                gustVelocity = Vector3.zero,
                atmosphereVelocity = Vector3.zero,
                hasAtmosphere = airDensity > 0.0001f
            };
        }

        public void SetEnvironmentProvider(
            IPlanetEnvironmentProvider provider)
        {
            environmentProvider = provider;
        }

        public void ReloadArcadeFlightTuning()
        {
            ReloadArcadeFlightTuning(true);
        }

        public void ReloadArcadeFlightTuning(
            bool applyRuntimeOverrides)
        {
            if (arcadeFlightTuning != null &&
                (arcadeFlightTuning.hideFlags & HideFlags.DontSave) != 0)
            {
                Destroy(arcadeFlightTuning);
            }
            arcadeFlightTuning =
                ArcadeFlightTuningProfile.CreateRuntimeCopy(
                    applyRuntimeOverrides);
            arcadeFlightTuningResolved = true;
        }

        public void SetInjectedControl(RobocraftControlFrame control)
        {
            injectedControlFrame = control;
            injectedControlActive = true;
        }

        public void ClearInjectedControl()
        {
            injectedControlFrame = default;
            injectedControlActive = false;
        }

        ArcadeFlightTuningProfile PlayerArcadeTuning
        {
            get
            {
                if (!arcadeFlightTuningResolved)
                    ReloadArcadeFlightTuning();
                return arcadeFlightTuning;
            }
        }

        public void ClearPlanetEnvironment()
        {
            gravity = Vector3.down * 9.81f;
            airDensity = 1.225f;
            altitude = 0f;
            environmentSample = PlanetEnvironmentSample.EarthLike(
                Vector3.down * 9.81f,
                0f);
            environmentProvider = null;
        }

        public void QueueExternalImpulse(
            Vector3 worldImpulse,
            Vector3 worldPosition)
        {
            if (body == null ||
                !OwnsPhysics ||
                body.isKinematic ||
                !VehicleWholeBodyAudit.Finite(worldImpulse) ||
                !VehicleWholeBodyAudit.Finite(worldPosition))
                return;

            queuedExternalImpulseWorld += worldImpulse;
            queuedExternalAngularImpulseWorld += Vector3.Cross(
                worldPosition - body.worldCenterOfMass,
                worldImpulse);
        }

        public bool TryApplyExternalImpulseInCurrentPhysicsStep(
            Vector3 worldImpulse,
            Vector3 worldPosition)
        {
            if (body == null ||
                !OwnsPhysics ||
                body.isKinematic ||
                !VehicleWholeBodyAudit.Finite(worldImpulse) ||
                !VehicleWholeBodyAudit.Finite(worldPosition))
            {
                return false;
            }

            RouteExternalImpulse(
                worldImpulse,
                worldPosition,
                Time.inFixedTimeStep,
                Time.fixedTimeAsDouble);
            return true;
        }

        void RouteExternalImpulse(
            Vector3 worldImpulse,
            Vector3 worldPosition,
            bool inFixedTimeStep,
            double fixedStepTime)
        {
            float step = Mathf.Max(
                0.0001f,
                Time.fixedDeltaTime);
            if (physicsStepRunning)
            {
                if (ledger.AddImpulseAtPosition(
                        worldImpulse,
                        worldPosition,
                        step))
                {
                    return;
                }

                ApplyLateExternalImpulse(
                    worldImpulse,
                    worldPosition,
                    step);
                return;
            }

            if (inFixedTimeStep &&
                physicsStepCompleted &&
                SamePhysicsStep(
                    completedPhysicsStepTime,
                    fixedStepTime))
            {
                ApplyLateExternalImpulse(
                    worldImpulse,
                    worldPosition,
                    step);
                return;
            }

            QueueExternalImpulse(
                worldImpulse,
                worldPosition);
        }

        void ApplyLateExternalImpulse(
            Vector3 worldImpulse,
            Vector3 worldPosition,
            float step)
        {
            body.AddForceAtPosition(
                worldImpulse,
                worldPosition,
                ForceMode.Impulse);
            if (ledger.RecordAppliedImpulseAtPosition(
                    worldImpulse,
                    worldPosition,
                    step))
            {
                UpdateWholeBodyAudit();
            }
        }

        static bool SamePhysicsStep(
            double first,
            double second)
        {
            return !double.IsNaN(first) &&
                   !double.IsInfinity(first) &&
                   !double.IsNaN(second) &&
                   !double.IsInfinity(second) &&
                   first == second;
        }

        void ClearQueuedExternalImpulses()
        {
            queuedExternalImpulseWorld = Vector3.zero;
            queuedExternalAngularImpulseWorld = Vector3.zero;
            physicsStepRunning = false;
            physicsStepCompleted = false;
            activePhysicsStepTime = 0d;
            completedPhysicsStepTime = 0d;
        }

        public void SetDamageDisabled(bool value)
        {
            damageDisabled = value;
            if (value)
            {
                ResetEvasion();
                ZeroAirThrottle();
            }
        }

        public void QueueStructureRebuild(VehicleStructureDelta delta)
        {
            rebuildPending = true;
        }

        void Update()
        {
            if (!IsActive ||
                diagnosticFlight ||
                !controlsEnabled ||
                injectedControlActive ||
                body == null ||
                body.isKinematic ||
                evasionState != VehicleEvasionState.Ready ||
                !Input.GetKeyDown(KeyCode.F))
                return;

            RequestEvasion(new Vector2(
                Key(KeyCode.D, KeyCode.A),
                Key(KeyCode.W, KeyCode.S)));
        }

        public bool RequestEvasion(Vector2 direction)
        {
            if (!IsActive ||
                body == null ||
                body.isKinematic ||
                evasionState != VehicleEvasionState.Ready ||
                direction.sqrMagnitude < 0.0001f)
                return false;

            pendingEvasionDirection =
                Vector2.ClampMagnitude(direction, 1f).normalized;
            pendingEvasionRequest = true;
            return true;
        }

        void FixedUpdate()
        {
            bool evadeRequested = pendingEvasionRequest;
            Vector2 evadeDirection = pendingEvasionDirection;
            pendingEvasionRequest = false;
            pendingEvasionDirection = Vector2.zero;
            bool injected = !controlsEnabled || injectedControlActive;
            bool tuningInputCaptured =
                ArcadeFlightRuntimeTuningOverlay.IsInputCaptured &&
                controlsEnabled &&
                !injectedControlActive;
            RobocraftControlFrame control = !controlsEnabled
                ? default
                : injectedControlActive
                    ? injectedControlFrame
                    : tuningInputCaptured
                        ? default
                        : ReadPilotControl(
                            evadeRequested,
                            evadeDirection);
            bool allowAimOverride = injected;
            if (controlsEnabled &&
                !injectedControlActive &&
                !tuningInputCaptured &&
                pilotAimSource != null &&
                pilotAimSource.TryGetPilotAim(out Vector3 pilotAim) &&
                pilotAim.sqrMagnitude > 0.0001f)
            {
                control.hasAimOverride = true;
                control.aimForwardWorld = pilotAim.normalized;
                allowAimOverride = true;
            }
            StepPhysics(control, allowAimOverride);
        }

        void ResolvePilotAimSource()
        {
            pilotAimSource = GetComponent(
                typeof(IRobocraftPilotAimSource))
                as IRobocraftPilotAimSource;
        }

        static RobocraftControlFrame ReadPilotControl(
            bool evadeRequested,
            Vector2 evadeDirection)
        {
            return new RobocraftControlFrame
            {
                move = new Vector2(
                    Key(KeyCode.D, KeyCode.A),
                    Key(KeyCode.W, KeyCode.S)),
                vertical =
                    KeyboardMouseFlightInput.ResolveVerticalAxis(
                        Input.GetKey(KeyCode.Space),
                        Input.GetKey(KeyCode.LeftControl),
                        Input.GetKey(KeyCode.RightControl)),
                roll = Key(KeyCode.E, KeyCode.Q),
                boost = Input.GetKey(KeyCode.LeftShift) ||
                        Input.GetKey(KeyCode.RightShift),
                braking = Input.GetKey(KeyCode.X),
                freeLook = Input.GetKey(KeyCode.LeftAlt) ||
                           Input.GetKey(KeyCode.RightAlt),
                hasAimOverride = false,
                evadeRequested = evadeRequested,
                evadeDirection = evadeDirection
            };
        }

        public void SimulateDiagnosticStep(
            RobocraftControlFrame control)
        {
            StepPhysics(control, true);
        }

        void StepPhysics(
            RobocraftControlFrame control,
            bool injectedControl)
        {
            if (body == null)
                return;
            if (rebuildPending)
            {
                rebuildPending = false;
                Rebuild();
            }
            if (!OwnsPhysics || body.isKinematic)
                return;

            activePhysicsStepTime = Time.fixedTimeAsDouble;
            try
            {
            IPlanetEnvironmentProvider provider =
                environmentProvider ?? PlanetEnvironmentRuntime.Active;
            environmentSample = provider != null
                ? provider.Sample(
                    body.worldCenterOfMass,
                    Time.fixedTimeAsDouble)
                : environmentSample;
            gravity = environmentSample.gravityAcceleration;
            airDensity = Mathf.Max(0f, environmentSample.airDensity);
            altitude = Mathf.Max(0f, environmentSample.altitude);
            BuildMoverForceScales();
            BuildPropellerWashes();
            ConfigureAirflow(provider);
            rc3Physics.PrepareAerodynamics(
                body,
                transform,
                airDensity,
                airflowField,
                environmentSample);

            ledger.Begin(body);
            physicsStepRunning = true;
            ledger.AddAcceleration(gravity);
            ApplyQueuedExternalImpulses();
            Vector3 up = gravity.sqrMagnitude > 0.001f
                ? -gravity.normalized
                : Vector3.up;
            groundMobility.BeginPhysicsStep(
                up,
                gravity.magnitude,
                Time.fixedDeltaTime,
                ledger);
            if (damageDisabled)
            {
                plannedAeroTorqueLocal = Vector3.zero;
                trimTorqueLocal = Vector3.zero;
                ResetControlAudit();
                SetAirThrottleZero();
                groundMobility.ApplyPassive(
                    up,
                    ledger,
                    Time.fixedDeltaTime);
                ApplyAerodynamics();
                groundMobility.FinalizePhysicsStep(
                    up,
                    gravity.magnitude,
                    ledger,
                    Time.fixedDeltaTime);
                UpdateWholeBodyAudit();
                ledger.Apply();
                return;
            }

            groundMobility.ApplyControl(
                up,
                gravity.magnitude,
                control.move,
                control.braking,
                control.boost,
                ledger,
                Time.fixedDeltaTime);
            Vector3? aimOverride = injectedControl &&
                                   control.hasAimOverride &&
                                   control.aimForwardWorld.sqrMagnitude > 0.0001f
                ? control.aimForwardWorld
                : (Vector3?)null;
            bool suppressOrdinaryControl;
            if (groundMobility.HasStableGroundSupport)
            {
                if (evasionState == VehicleEvasionState.Active ||
                    evasionState == VehicleEvasionState.Recovery)
                {
                    CancelEvasionForGroundContact();
                }
                suppressOrdinaryControl = false;
            }
            else
            {
                suppressOrdinaryControl =
                    UpdateEvasion(control, up, aimOverride);
            }
            if (suppressOrdinaryControl)
                SuppressControlsForEvasion();
            else
            {
                ApplyAirMovement(
                    up,
                    control.move,
                    control.vertical,
                    control.roll,
                    control.braking,
                    control.boost,
                    control.freeLook,
                    aimOverride,
                    groundMobility.GroundControlWeight);
            }
            ApplyAerodynamics();
            groundMobility.FinalizePhysicsStep(
                up,
                gravity.magnitude,
                ledger,
                Time.fixedDeltaTime);
            if (groundMobility.HasStableGroundSupport &&
                (evasionState == VehicleEvasionState.Active ||
                 evasionState == VehicleEvasionState.Recovery))
            {
                CancelEvasionForGroundContact();
                groundMobility.FinalizePhysicsStep(
                    up,
                    gravity.magnitude,
                    ledger,
                    Time.fixedDeltaTime);
            }
            UpdateWholeBodyAudit();
            ledger.Apply();
            }
            finally
            {
                physicsStepRunning = false;
                physicsStepCompleted = true;
                completedPhysicsStepTime =
                    activePhysicsStepTime;
            }
        }

        bool UpdateEvasion(
            RobocraftControlFrame control,
            Vector3 up,
            Vector3? aimForwardOverride)
        {
            float step = Mathf.Max(0.0001f, Time.fixedDeltaTime);
            bool actionWasActive =
                evasionState == VehicleEvasionState.Active ||
                evasionState == VehicleEvasionState.Recovery;

            if (evasionState != VehicleEvasionState.Ready)
            {
                evasionCooldownRemaining = Mathf.Max(
                    0f,
                    evasionCooldownRemaining - step);
            }

            if (measureEvasionVelocity)
            {
                evasionActualSpeedDelta = Mathf.Max(
                    0f,
                    Vector3.Dot(
                        body.velocity - velocityBeforeEvasionImpulse,
                        evasionWorldDirection));
                measureEvasionVelocity = false;
            }

            if (evasionState == VehicleEvasionState.Ready &&
                control.evadeRequested &&
                control.evadeDirection.sqrMagnitude > 0.0001f)
            {
                StartEvasion(
                    control.evadeDirection,
                    up,
                    aimForwardOverride);
                actionWasActive =
                    evasionState == VehicleEvasionState.Active;
            }
            else if (evasionState == VehicleEvasionState.Active)
            {
                evasionActionElapsed += step;
                float signedRollSpeed =
                    Vector3.Dot(
                        body.angularVelocity,
                        evasionRollAxisWorld) *
                    evasionRollSign;
                evasionAccumulatedRollRadians +=
                    Mathf.Max(0f, signedRollSpeed) * step;
                float remainingRoll = Mathf.Max(
                    0f,
                    2f * Mathf.PI -
                    evasionAccumulatedRollRadians);
                if (remainingRoll <= 0.02f ||
                    evasionActionElapsed >=
                    EvasionRollDuration + 0.12f)
                {
                    ApplyEvasionRollCorrection();
                    evasionRecoveryElapsed = 0f;
                    evasionState = VehicleEvasionState.Recovery;
                }
                else
                {
                    float targetRollSpeed = Mathf.Min(
                        EvasionRollSpeed,
                        remainingRoll / step);
                    float currentRollSpeed = Vector3.Dot(
                        body.angularVelocity,
                        evasionRollAxisWorld);
                    AddAngularVelocityImpulse(
                        evasionRollAxisWorld *
                        (evasionRollSign *
                         targetRollSpeed -
                         currentRollSpeed));
                }
            }
            else if (evasionState == VehicleEvasionState.Recovery)
            {
                evasionActionElapsed += step;
                evasionRecoveryElapsed += step;
                ApplyEvasionOrientationRecovery(step);
                if (evasionRecoveryElapsed >=
                    EvasionRecoveryDuration)
                {
                    evasionState = evasionCooldownRemaining > 0f
                        ? VehicleEvasionState.Cooldown
                        : VehicleEvasionState.Ready;
                    evasionPresentation?.EndAction();
                }
            }
            else if (evasionState == VehicleEvasionState.Cooldown &&
                     evasionCooldownRemaining <= 0f)
            {
                evasionState = VehicleEvasionState.Ready;
                evasionWorldDirection = Vector3.zero;
                evasionRollAxisWorld = Vector3.zero;
            }

            RefreshEvasionSnapshot();
            return actionWasActive ||
                   evasionState == VehicleEvasionState.Active ||
                   evasionState == VehicleEvasionState.Recovery;
        }

        void StartEvasion(
            Vector2 lockedDirection,
            Vector3 up,
            Vector3? aimForwardOverride)
        {
            Vector3 planarForward = aimForwardOverride.HasValue
                ? Vector3.ProjectOnPlane(
                    aimForwardOverride.Value,
                    up)
                : CameraPlanarForward(up);
            if (planarForward.sqrMagnitude < 0.0001f)
                planarForward = CameraPlanarForward(up);
            planarForward.Normalize();
            Vector3 planarRight =
                Vector3.Cross(up, planarForward).normalized;
            Vector2 direction2D =
                Vector2.ClampMagnitude(lockedDirection, 1f).normalized;
            Vector3 worldDirection =
                planarForward * direction2D.y +
                planarRight * direction2D.x;
            if (worldDirection.sqrMagnitude < 0.0001f)
                return;

            evasionWorldDirection = worldDirection.normalized;
            evasionRollAxisWorld = transform.forward.sqrMagnitude > 0.0001f
                ? transform.forward.normalized
                : planarForward;
            evasionStartRotation = body.rotation;
            evasionBaselineAngularVelocity = body.angularVelocity;
            evasionBaselineRollSpeed = Vector3.Dot(
                body.angularVelocity,
                evasionRollAxisWorld);
            evasionActionElapsed = 0f;
            evasionRecoveryElapsed = 0f;
            evasionAccumulatedRollRadians = 0f;
            evasionCooldownRemaining = EvasionCooldownDuration;
            evasionActualSpeedDelta = 0f;
            evasionState = VehicleEvasionState.Active;

            float rollSign;
            if (Mathf.Abs(direction2D.x) > 0.001f)
                rollSign = direction2D.x < 0f ? 1f : -1f;
            else
                rollSign = direction2D.y >= 0f ? -1f : 1f;
            evasionRollSign = rollSign;

            float inverseStep = 1f / Mathf.Max(
                0.0001f,
                Time.fixedDeltaTime);
            Vector3 linearImpulse =
                evasionWorldDirection * body.mass * EvasionSpeedDelta;
            ledger.AddForce(linearImpulse * inverseStep);

            float currentRollSpeed = Vector3.Dot(
                body.angularVelocity,
                evasionRollAxisWorld);
            float targetRollSpeed = rollSign * EvasionRollSpeed;
            AddAngularVelocityImpulse(
                evasionRollAxisWorld *
                (targetRollSpeed - currentRollSpeed));

            velocityBeforeEvasionImpulse = body.velocity;
            measureEvasionVelocity = true;
            hoverHeld = false;
            EnsureEvasionPresentation();
            if (!diagnosticFlight)
            {
                evasionPresentation?.BeginEvasion(
                    evasionWorldDirection,
                    rollSign);
            }
            RefreshEvasionSnapshot();
        }

        void ApplyEvasionRollCorrection()
        {
            if (body == null ||
                evasionRollAxisWorld.sqrMagnitude < 0.0001f)
                return;
            float currentRollSpeed = Vector3.Dot(
                body.angularVelocity,
                evasionRollAxisWorld);
            AddAngularVelocityImpulse(
                evasionRollAxisWorld *
                (evasionBaselineRollSpeed - currentRollSpeed));
        }

        void ApplyEvasionOrientationRecovery(float step)
        {
            if (body == null)
                return;

            Quaternion error =
                evasionStartRotation *
                Quaternion.Inverse(body.rotation);
            if (error.w < 0f)
            {
                error = new Quaternion(
                    -error.x,
                    -error.y,
                    -error.z,
                    -error.w);
            }

            error.ToAngleAxis(
                out float angleDegrees,
                out Vector3 axisWorld);
            if (angleDegrees > 180f)
                angleDegrees -= 360f;
            if (!VehicleWholeBodyAudit.Finite(axisWorld) ||
                !float.IsFinite(angleDegrees))
                return;

            float remainingTime = Mathf.Max(
                step,
                EvasionRecoveryDuration -
                evasionRecoveryElapsed +
                step);
            Vector3 correctionOmega =
                axisWorld.normalized *
                (angleDegrees * Mathf.Deg2Rad /
                 remainingTime);
            correctionOmega = Vector3.ClampMagnitude(
                correctionOmega,
                EvasionRollSpeed * 1.35f);
            Vector3 targetOmega =
                evasionBaselineAngularVelocity +
                correctionOmega;
            AddAngularVelocityImpulse(
                targetOmega - body.angularVelocity);
        }

        void AddAngularVelocityImpulse(Vector3 deltaOmegaWorld)
        {
            if (body == null ||
                deltaOmegaWorld.sqrMagnitude < 0.0000001f ||
                !VehicleWholeBodyAudit.Finite(deltaOmegaWorld))
                return;

            Quaternion principalWorld =
                body.rotation * body.inertiaTensorRotation;
            Vector3 principalDelta =
                Quaternion.Inverse(principalWorld) * deltaOmegaWorld;
            Vector3 principalImpulse = Vector3.Scale(
                body.inertiaTensor,
                principalDelta);
            Vector3 angularImpulse =
                principalWorld * principalImpulse;
            if (!VehicleWholeBodyAudit.Finite(angularImpulse))
                return;

            ledger.AddTorque(
                angularImpulse /
                Mathf.Max(0.0001f, Time.fixedDeltaTime));
        }

        void SuppressControlsForEvasion()
        {
            hoverHeld = false;
            ResetTrainingHold();
            pilotRotationActive = true;
            plannedAeroTorqueLocal =
                rc3Physics.AllocateControlSurfaceTorque(
                    Vector3.zero,
                    Time.fixedDeltaTime);
            SetAirThrottleZero();
            ResetControlAudit();
        }

        void RefreshEvasionSnapshot()
        {
            evasionSnapshot = new VehicleEvasionSnapshot
            {
                state = evasionState,
                actionTimeRemaining =
                    evasionState == VehicleEvasionState.Active ||
                    evasionState == VehicleEvasionState.Recovery
                        ? Mathf.Max(
                            0f,
                            EvasionActionDuration -
                            evasionActionElapsed)
                        : 0f,
                cooldownTimeRemaining = evasionState ==
                                        VehicleEvasionState.Ready
                    ? 0f
                    : evasionCooldownRemaining,
                worldDirection = evasionWorldDirection,
                requestedSpeedDelta = EvasionSpeedDelta,
                actualSpeedDelta = evasionActualSpeedDelta,
                rollProgress = Mathf.Clamp01(
                    evasionAccumulatedRollRadians /
                    (2f * Mathf.PI))
            };
        }

        void CancelEvasionForGroundContact()
        {
            if (body != null &&
                evasionRollAxisWorld.sqrMagnitude > 0.0001f)
            {
                Vector3 axis = evasionRollAxisWorld.normalized;
                float currentRollSpeed =
                    Vector3.Dot(body.angularVelocity, axis);
                float baselineRollSpeed =
                    Vector3.Dot(
                        evasionBaselineAngularVelocity,
                        axis);
                float deltaRollSpeed = Mathf.Clamp(
                    baselineRollSpeed - currentRollSpeed,
                    -EvasionRollSpeed * 1.25f,
                    EvasionRollSpeed * 1.25f);
                Quaternion inertiaWorldRotation =
                    body.rotation * body.inertiaTensorRotation;
                Vector3 localDelta =
                    Quaternion.Inverse(inertiaWorldRotation) *
                    (axis * deltaRollSpeed);
                Vector3 inertia = body.inertiaTensor;
                Vector3 angularImpulseWorld =
                    inertiaWorldRotation *
                    Vector3.Scale(localDelta, inertia);
                ledger.AddTorque(
                    angularImpulseWorld /
                    Mathf.Max(
                        0.0001f,
                        Time.fixedDeltaTime));
            }
            ResetEvasion();
        }

        void ResetEvasion()
        {
            pendingEvasionRequest = false;
            pendingEvasionDirection = Vector2.zero;
            evasionState = VehicleEvasionState.Ready;
            evasionWorldDirection = Vector3.zero;
            evasionRollAxisWorld = Vector3.zero;
            evasionStartRotation = Quaternion.identity;
            evasionBaselineAngularVelocity = Vector3.zero;
            evasionBaselineRollSpeed = 0f;
            evasionRollSign = 0f;
            evasionAccumulatedRollRadians = 0f;
            evasionRecoveryElapsed = 0f;
            evasionActionElapsed = 0f;
            evasionCooldownRemaining = 0f;
            evasionActualSpeedDelta = 0f;
            measureEvasionVelocity = false;
            RefreshEvasionSnapshot();
            evasionPresentation?.CancelEvasion();
        }

        void EnsureEvasionPresentation()
        {
            if (evasionPresentation == null)
            {
                evasionPresentation =
                    GetComponent<VehicleEvasionPresentation>();
                if (evasionPresentation == null)
                {
                    evasionPresentation =
                        gameObject.AddComponent<
                            VehicleEvasionPresentation>();
                }
            }
            evasionPresentation.Bind(this, transform);
        }

        void ApplyQueuedExternalImpulses()
        {
            float inverseStep = 1f / Mathf.Max(
                0.0001f,
                Time.fixedDeltaTime);
            ledger.AddForce(queuedExternalImpulseWorld * inverseStep);
            ledger.AddTorque(
                queuedExternalAngularImpulseWorld * inverseStep);
            queuedExternalImpulseWorld = Vector3.zero;
            queuedExternalAngularImpulseWorld = Vector3.zero;
        }

        void UpdateWholeBodyAudit()
        {
            VehicleWholeBodyAuditSnapshot audit =
                VehicleWholeBodyAudit.Evaluate(
                    body,
                    ledger,
                    physicsOwnershipExclusive,
                    telemetry.inertiaTriangleValid);
            telemetry.netForceWorld = audit.netForceWorld;
            telemetry.netTorqueWorld = audit.netTorqueWorld;
            telemetry.predictedLinearAccelerationWorld =
                audit.predictedLinearAccelerationWorld;
            telemetry.predictedAngularAccelerationWorld =
                audit.predictedAngularAccelerationWorld;
            telemetry.requestedControlForceWorld =
                requestedControlForceWorld;
            telemetry.requestedControlTorqueWorld =
                requestedControlTorqueWorld;
            telemetry.actualControlForceWorld =
                actualControlForceWorld;
            telemetry.actualControlTorqueWorld =
                actualControlTorqueWorld;
            telemetry.controlForceResidualWorld =
                requestedControlForceWorld -
                actualControlForceWorld;
            telemetry.controlTorqueResidualWorld =
                requestedControlTorqueWorld -
                actualControlTorqueWorld;
            float forceScale = Mathf.Max(
                1f,
                Mathf.Max(
                    requestedControlForceWorld.magnitude,
                    actualControlForceWorld.magnitude));
            float torqueScale = Mathf.Max(
                1f,
                Mathf.Max(
                    requestedControlTorqueWorld.magnitude,
                    actualControlTorqueWorld.magnitude));
            float normalizedResidual = Mathf.Max(
                telemetry.controlForceResidualWorld.magnitude /
                forceScale,
                telemetry.controlTorqueResidualWorld.magnitude /
                torqueScale);
            telemetry.controlClosureRatio =
                Mathf.Clamp01(1f - normalizedResidual);
            telemetry.controlRequestSatisfied =
                VehicleWholeBodyAudit.Finite(normalizedResidual) &&
                normalizedResidual <= 0.02f;
            telemetry.wholeBodyValid = audit.valid;
            if (!audit.valid)
                telemetry.physicsAudit = audit.message;
            else if (!telemetry.controlRequestSatisfied)
                telemetry.physicsAudit =
                    $"RC3.2 control authority limited; " +
                    $"closure={telemetry.controlClosureRatio:P0}.";
            else
                telemetry.physicsAudit = audit.message;
        }

        void ResetControlAudit()
        {
            requestedControlForceWorld = Vector3.zero;
            requestedControlTorqueWorld = Vector3.zero;
            actualControlForceWorld = Vector3.zero;
            actualControlTorqueWorld = Vector3.zero;
        }

        static float Key(KeyCode positive, KeyCode negative)
        {
            return (Input.GetKey(positive) ? 1f : 0f) -
                   (Input.GetKey(negative) ? 1f : 0f);
        }

void ApplyAirMovement(
            Vector3 up,
            Vector2 move,
            float vertical,
            float roll,
            bool braking,
            bool boost,
            bool freeLook,
            Vector3? aimForwardOverride,
            float groundControlWeight)
        {
            float airControlScale =
                1f - Mathf.Clamp01(groundControlWeight);
            float hoverControlScale = Mathf.Min(
                airControlScale,
                Mathf.Clamp01(
                    1f - groundMobility.SupportRatio));
            float planarBrakeScale =
                groundMobility.GroundedWheelCount == 0
                    ? airControlScale
                    : 0f;
            Vector3 cameraForward = aimForwardOverride.HasValue
                ? Vector3.ProjectOnPlane(
                    aimForwardOverride.Value,
                    up).normalized
                : CameraPlanarForward(up);
            if (cameraForward.sqrMagnitude < 0.0001f)
                cameraForward = CameraPlanarForward(up);
            Vector3 cameraRight =
                Vector3.Cross(up, cameraForward).normalized;
            rcs24.BeginStep(moverForceScales);
            bool prioritizePlayerArcadeIntent =
                !braking &&
                TrainingFlightAssist.ShouldPrioritizePlayerIntent(
                    coreAssistMode,
                    controlsEnabled,
                    injectedControlActive);
            ArcadeFlightTuningProfile playerArcadeTuning =
                prioritizePlayerArcadeIntent
                    ? PlayerArcadeTuning
                    : null;

            float resolvedRoll = coreAssistMode ==
                                 VehicleCoreAssistMode.Training &&
                                 !freeLook
                ? Mathf.Clamp(
                    roll + move.x *
                    (playerArcadeTuning != null
                        ? playerArcadeTuning.strafeRollCoupling
                        : 0.22f),
                    -1f,
                    1f)
                : roll;
            Vector3 requestedWorldTorque = ResolveOrientationTorque(
                up,
                resolvedRoll,
                freeLook,
                braking,
                aimForwardOverride);
            Vector3 requestedLocalTorque =
                transform.InverseTransformDirection(requestedWorldTorque);
            requestedLocalTorque = rc3Physics.ShapeManeuverTorque(
                requestedLocalTorque,
                coreAssistMode);
            Vector3 totalAuthority =
                AngularAuthority() + rc3Physics.ControlTorqueAuthority;
            UpdateTrimTorque(totalAuthority);
            requestedLocalTorque += trimTorqueLocal;
            if (playerArcadeTuning != null)
            {
                requestedLocalTorque *=
                    playerArcadeTuning.aimTorqueMultiplier;
            }
            requestedLocalTorque *= airControlScale;
            plannedAeroTorqueLocal =
                rc3Physics.AllocateControlSurfaceTorque(
                    requestedLocalTorque,
                    Time.fixedDeltaTime);
            Vector3 remainingLocalTorque =
                requestedLocalTorque - plannedAeroTorqueLocal;
            if (!prioritizePlayerArcadeIntent)
            {
                rcs24.SolveRotation(new Rcs24SolveRequest
                {
                    desired = remainingLocalTorque,
                    strictDirection = true,
                    group = "rotation"
                });
            }
            Vector3 desiredLocalForce = Vector3.zero;

            if (braking &&
                Mathf.Max(
                    hoverControlScale,
                    planarBrakeScale) > 0.001f)
            {
                if (!hoverHeld)
                {
                    hoverPosition = body.position;
                    hoverHeld = true;
                }
                if (coreAssistMode == VehicleCoreAssistMode.Training)
                {
                    trainingHoldPosition = hoverPosition;
                    trainingHoldInitialized = true;
                }
                float heightError =
                    Vector3.Dot(hoverPosition - body.position, up);
                float verticalSpeed = Vector3.Dot(body.velocity, up);
                float hoverForce = Mathf.Max(
                    0f,
                    body.mass *
                     (gravity.magnitude + heightError * 2f -
                      verticalSpeed * 3.2f)) *
                    hoverControlScale;
                Vector3 hoverLocal =
                    transform.InverseTransformDirection(up * hoverForce);
                Rcs24SolveResult hoverResult = rcs24.SolveHover(
                    new Rcs24SolveRequest
                    {
                        desired = hoverLocal,
                        strictDirection = true,
                        group = "hover_vertical"
                    });
                if (hoverResult.commonScale > 0f &&
                    hoverResult.missingAxisMask == 0)
                {
                    desiredLocalForce = hoverLocal;
                }

                Vector3 planarError = Vector3.ProjectOnPlane(
                    hoverPosition - body.position,
                    up);
                Vector3 planarVelocity = Vector3.ProjectOnPlane(
                    body.velocity,
                    up);
                Vector3 horizontalBrake =
                    planarError * body.mass * 0.8f -
                    planarVelocity * body.mass * 3.2f;
                horizontalBrake *= planarBrakeScale;
                if (horizontalBrake.sqrMagnitude > 0.01f)
                {
                    Vector3 combinedTarget = desiredLocalForce +
                        transform.InverseTransformDirection(
                            horizontalBrake);
                    Rcs24SolveResult brakeResult =
                        rcs24.SolveTranslation(
                            new Rcs24SolveRequest
                            {
                                desired = combinedTarget,
                                strictDirection = true,
                                group = "hover_horizontal"
                            });
                    if (brakeResult.commonScale > 0f &&
                        brakeResult.missingAxisMask == 0)
                    {
                        desiredLocalForce = combinedTarget;
                    }
                }
            }
            else if (coreAssistMode == VehicleCoreAssistMode.Training)
            {
                hoverHeld = false;
                Vector3 arcadeForward = aimForwardOverride.HasValue
                    ? aimForwardOverride.Value
                    : CameraForward();
                if (arcadeForward.sqrMagnitude < 0.0001f)
                    arcadeForward = transform.forward;
                arcadeForward.Normalize();
                Vector3 arcadeRight = Vector3.Cross(
                    up,
                    arcadeForward);
                if (arcadeRight.sqrMagnitude < 0.0001f)
                    arcadeRight = cameraRight;
                else
                    arcadeRight.Normalize();
                Vector3 arcadeMovementInput = Vector3.ClampMagnitude(
                    arcadeForward * move.y +
                    arcadeRight * move.x +
                    up * vertical,
                    1f);
                float commandAcceleration =
                    CalculateArcadeCommandAcceleration(
                        new Vector3(move.x, vertical, move.y));
                if (prioritizePlayerArcadeIntent)
                {
                    desiredLocalForce =
                        AllocatePlayerArcadeIntentPriority(
                            up,
                            arcadeMovementInput,
                            commandAcceleration,
                            boost,
                            airControlScale,
                            hoverControlScale,
                            remainingLocalTorque,
                            playerArcadeTuning);
                }
                else
                {
                    desiredLocalForce = CalculateArcadeDesiredLocalForce(
                        up,
                        arcadeMovementInput,
                        commandAcceleration,
                        boost,
                        airControlScale,
                        hoverControlScale);
                    if (desiredLocalForce.sqrMagnitude > 0.01f)
                    {
                        Rcs24SolveResult translationResult =
                            rcs24.SolveTranslation(
                                new Rcs24SolveRequest
                                {
                                    desired = desiredLocalForce,
                                    strictDirection = true,
                                    group = "arcade_velocity_hold"
                                });
                        if (translationResult.commonScale <= 0f ||
                            translationResult.missingAxisMask != 0)
                        {
                            desiredLocalForce = Vector3.zero;
                        }
                    }
                }
            }
            else
            {
                hoverHeld = false;
                ResetTrainingHold();
                Vector3 cameraPlanarDirection =
                    cameraForward * move.y +
                    cameraRight * move.x;
                Vector3 vehicleForward = Vector3.ProjectOnPlane(
                    transform.forward,
                    up);
                if (vehicleForward.sqrMagnitude < 0.0001f)
                    vehicleForward = cameraForward;
                else
                    vehicleForward.Normalize();
                Vector3 groundPlanarDirection =
                    vehicleForward * move.y;
                Vector3 planarDirection = Vector3.Lerp(
                    cameraPlanarDirection,
                    groundPlanarDirection,
                    Mathf.Clamp01(
                        groundControlWeight * 2.5f));
                Vector3 direction = Vector3.ClampMagnitude(
                    planarDirection * airControlScale +
                    up * vertical,
                    1f);
                if (direction.sqrMagnitude > 0.0001f)
                {
                    float requestedThrottle =
                        Mathf.Abs(vertical) > 0.01f || boost
                            ? 1f
                            : CruiseThrottle;
                    Vector3 target = direction *
                        CalculateDirectionalForce(direction) *
                        requestedThrottle;
                    Vector3 localTarget =
                        transform.InverseTransformDirection(target);
                    Rcs24SolveResult translationResult =
                        rcs24.SolveTranslation(
                            new Rcs24SolveRequest
                            {
                                desired = localTarget,
                                strictDirection = true,
                                group = "player_translation"
                            });
                    if (translationResult.commonScale > 0f &&
                        translationResult.missingAxisMask == 0)
                    {
                        desiredLocalForce = localTarget;
                    }
                }
            }

            for (int correction = 0; correction < 2; correction++)
            {
                rcs24.SolveRotation(new Rcs24SolveRequest
                {
                    desired = remainingLocalTorque,
                    strictDirection = false,
                    group = "wrench_rotation_correction"
                });
                rcs24.SolveTranslation(new Rcs24SolveRequest
                {
                    desired = desiredLocalForce,
                    strictDirection = false,
                    group = "wrench_force_correction"
                });
            }

            Rcs24SolveResult output =
                rcs24.CompleteStepBuffered(Time.fixedDeltaTime);
            requestedControlForceWorld =
                transform.TransformDirection(desiredLocalForce);
            requestedControlTorqueWorld =
                transform.TransformDirection(requestedLocalTorque);
            actualControlForceWorld =
                transform.TransformDirection(output.localForce);
            actualControlTorqueWorld = transform.TransformDirection(
                output.localTorque + plannedAeroTorqueLocal);
            ledger.AddForce(actualControlForceWorld);
            ledger.AddTorque(
                transform.TransformDirection(output.localTorque));
            UpdateAirMoverOutputs(output.thrusterThrottles);
        }

        Vector3 AllocatePlayerArcadeIntentPriority(
            Vector3 up,
            Vector3 movementInputWorld,
            float commandAcceleration,
            bool boost,
            float airControlScale,
            float hoverControlScale,
            Vector3 remainingLocalTorque,
            ArcadeFlightTuningProfile tuning)
        {
            float mass = Mathf.Max(1f, body.mass);
            float gravityMagnitude = Mathf.Max(0f, gravity.magnitude);
            Vector3 movementInput = Vector3.ClampMagnitude(
                movementInputWorld,
                1f);
            TrainingFlightAssistDemand demand = CalculateArcadeDemand(
                movementInput,
                commandAcceleration,
                boost,
                airControlScale,
                tuning);
            bool hasMovementIntent =
                TrainingFlightAssist.HasMovementIntent(
                    movementInput,
                    tuning);
            float gravitySupport = tuning != null
                ? hasMovementIntent
                    ? tuning.movingGravitySupport
                    : tuning.idleGravitySupport
                : 1f;

            Vector3 cumulativeForceWorld =
                up * gravityMagnitude * hoverControlScale *
                gravitySupport * mass;
            Vector3 cumulativeLocalForce =
                transform.InverseTransformDirection(
                    cumulativeForceWorld);
            SolvePlayerArcadeTranslation(
                cumulativeLocalForce,
                "arcade_gravity_support");

            if (hasMovementIntent)
            {
                Vector3 intentForceWorld = LimitArcadeForceToAuthority(
                    demand.intentAccelerationWorld *
                    airControlScale * mass,
                    tuning != null
                        ? tuning.intentAuthorityFraction
                        : TrainingFlightAssist.IntentAuthorityFraction);
                cumulativeForceWorld += intentForceWorld;
                cumulativeLocalForce =
                    transform.InverseTransformDirection(
                        cumulativeForceWorld);
                SolvePlayerArcadeTranslation(
                    cumulativeLocalForce,
                    "arcade_player_intent");

                rcs24.SolveRotation(new Rcs24SolveRequest
                {
                    desired = remainingLocalTorque,
                    strictDirection = true,
                    group = "arcade_rotation_after_intent"
                });

                Vector3 driftForceWorld = LimitArcadeForceToAuthority(
                    demand.driftCancellationAccelerationWorld *
                    airControlScale * mass,
                    tuning != null
                        ? tuning.driftAuthorityFraction
                        : 1f);
                cumulativeForceWorld += driftForceWorld;
                cumulativeLocalForce =
                    transform.InverseTransformDirection(
                        cumulativeForceWorld);
                SolvePlayerArcadeTranslation(
                    cumulativeLocalForce,
                    "arcade_drift_cancellation");
            }
            else
            {
                Vector3 stopForceWorld = LimitArcadeForceToAuthority(
                    demand.stopAccelerationWorld *
                    airControlScale * mass,
                    tuning != null
                        ? tuning.stopAuthorityFraction
                        : 1f);
                cumulativeForceWorld += stopForceWorld;
                cumulativeLocalForce =
                    transform.InverseTransformDirection(
                        cumulativeForceWorld);
                SolvePlayerArcadeTranslation(
                    cumulativeLocalForce,
                    "arcade_release_stop");

                rcs24.SolveRotation(new Rcs24SolveRequest
                {
                    desired = remainingLocalTorque,
                    strictDirection = true,
                    group = "arcade_rotation_after_stop"
                });

                Vector3 positionHoldForceWorld =
                    LimitArcadeForceToAuthority(
                        demand.positionHoldAccelerationWorld *
                        airControlScale * mass,
                        tuning != null
                            ? tuning.positionHoldAuthorityFraction
                            : 1f);
                cumulativeForceWorld += positionHoldForceWorld;
                cumulativeLocalForce =
                    transform.InverseTransformDirection(
                        cumulativeForceWorld);
                SolvePlayerArcadeTranslation(
                    cumulativeLocalForce,
                    "arcade_position_hold");
            }

            return cumulativeLocalForce;
        }

        void SolvePlayerArcadeTranslation(
            Vector3 cumulativeLocalForce,
            string group)
        {
            if (cumulativeLocalForce.sqrMagnitude <= 0.01f)
                return;
            rcs24.SolveTranslation(new Rcs24SolveRequest
            {
                desired = cumulativeLocalForce,
                strictDirection = false,
                group = group
            });
        }

        Vector3 LimitArcadeForceToAuthority(
            Vector3 requestedForceWorld,
            float authorityFraction)
        {
            if (requestedForceWorld.sqrMagnitude <= 0.0001f)
                return Vector3.zero;
            float maximumForce = CalculateDirectionalForce(
                requestedForceWorld.normalized) *
                Mathf.Clamp01(authorityFraction);
            return Vector3.ClampMagnitude(
                requestedForceWorld,
                maximumForce);
        }

        Vector3 CalculateArcadeDesiredLocalForce(
            Vector3 up,
            Vector3 movementInputWorld,
            float commandAcceleration,
            bool boost,
            float airControlScale,
            float hoverControlScale)
        {
            float mass = Mathf.Max(1f, body.mass);
            float gravityMagnitude = Mathf.Max(0f, gravity.magnitude);
            TrainingFlightAssistDemand demand = CalculateArcadeDemand(
                movementInputWorld,
                commandAcceleration,
                boost,
                airControlScale,
                null);

            Vector3 requestedAcceleration =
                demand.controlAccelerationWorld * airControlScale +
                up * gravityMagnitude * hoverControlScale;
            return transform.InverseTransformDirection(
                requestedAcceleration * mass);
        }

        TrainingFlightAssistDemand CalculateArcadeDemand(
            Vector3 movementInputWorld,
            float commandAcceleration,
            bool boost,
            float airControlScale,
            ArcadeFlightTuningProfile tuning)
        {
            Vector3 movementInput = Vector3.ClampMagnitude(
                movementInputWorld,
                1f);
            bool hasMovementIntent =
                TrainingFlightAssist.HasMovementIntent(
                    movementInput,
                    tuning);

            if (airControlScale < 0.5f &&
                !hasMovementIntent)
            {
                trainingHoldPosition = body.position;
                trainingHoldInitialized = true;
            }

            float targetSpeed = 0f;
            if (hasMovementIntent)
            {
                float netAcceleration = Mathf.Max(
                    0f,
                    commandAcceleration + Vector3.Dot(
                        gravity,
                        movementInput.normalized));
                targetSpeed =
                    TrainingFlightAssist.CalculateTargetSpeed(
                        netAcceleration,
                        boost,
                        tuning);
            }

            TrainingFlightAssistDemand demand =
                TrainingFlightAssist.CalculateDemand(
                    body.position,
                    body.velocity,
                    trainingHoldPosition,
                    trainingHoldInitialized,
                    movementInput,
                    targetSpeed,
                    tuning);
            trainingHoldPosition = demand.holdPosition;
            trainingHoldInitialized = true;
            return demand;
        }

        float CalculateArcadeCommandAcceleration(
            Vector3 localCommandAxes)
        {
            float inverseMass = 1f / Mathf.Max(1f, body.mass);
            Vector3 positive = new Vector3(
                CalculateDirectionalForce(transform.right),
                CalculateDirectionalForce(transform.up),
                CalculateDirectionalForce(transform.forward)) *
                inverseMass;
            Vector3 negative = new Vector3(
                CalculateDirectionalForce(-transform.right),
                CalculateDirectionalForce(-transform.up),
                CalculateDirectionalForce(-transform.forward)) *
                inverseMass;
            return TrainingFlightAssist.CalculateCommandAcceleration(
                localCommandAxes,
                positive,
                negative);
        }

        void ResetTrainingHold()
        {
            trainingHoldPosition = body != null
                ? body.position
                : transform.position;
            trainingHoldInitialized = false;
        }

        float[] BuildMoverForceScales()
        {
            if (moverForceScales.Length != airMovers.Count)
                moverForceScales = new float[airMovers.Count];
            for (int i = 0; i < airMovers.Count; i++)
                moverForceScales[i] = EffectiveThrustFactor(airMovers[i]);
            moverForceScalesValid = true;
            return moverForceScales;
        }

        float CachedMoverForceScale(int index)
        {
            return moverForceScalesValid
                && index >= 0
                && index < moverForceScales.Length
                    ? moverForceScales[index]
                    : EffectiveThrustFactor(airMovers[index]);
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

        float CalculateDirectionalForce(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude < 0.0001f)
                return 0f;

            Vector3 direction = worldDirection.normalized;
            float force = 0f;
            for (int index = 0; index < airMovers.Count; index++)
            {
                AirMover mover = airMovers[index];
                float alignment = Vector3.Dot(WorldDirection(mover), direction);
                if (alignment <= 0.0001f)
                    continue;
                force += mover.maximumForce *
                         alignment *
                         CachedMoverForceScale(index);
            }

            if (coreAssistMode == VehicleCoreAssistMode.Training)
            {
                Vector3 local =
                    transform.InverseTransformDirection(direction);
                force += Mathf.Abs(local.x) * trainingPlanarAssistForce;
                force += Mathf.Abs(local.z) * trainingPlanarAssistForce;
                force += local.y >= 0f
                    ? local.y * trainingUpAssistForce
                    : -local.y * trainingDownAssistForce;
            }

            return force;
        }

Vector3 ResolveOrientationTorque(
            Vector3 up,
            float roll,
            bool freeLook,
            bool braking,
            Vector3? aimForwardOverride)
        {
            if (aimForwardOverride.HasValue &&
                aimForwardOverride.Value.sqrMagnitude > 0.0001f)
            {
                heldAimForward = aimForwardOverride.Value.normalized;
            }
            else if (!freeLook)
            {
                heldAimForward = CameraForward();
            }
            Vector3 desiredForward =
                heldAimForward.sqrMagnitude > 0.001f
                    ? heldAimForward.normalized
                    : transform.forward;
            if (Mathf.Abs(Vector3.Dot(desiredForward, up)) > 0.995f)
            {
                desiredForward = Vector3.ProjectOnPlane(
                    transform.forward,
                    up);
                if (desiredForward.sqrMagnitude < 0.001f)
                {
                    desiredForward = Vector3.ProjectOnPlane(
                        Vector3.forward,
                        up);
                }
                desiredForward.Normalize();
            }
            Quaternion target =
                Quaternion.LookRotation(desiredForward, up) *
                Quaternion.AngleAxis(-roll * 28f, Vector3.forward);
            Quaternion error = target * Quaternion.Inverse(body.rotation);
            error.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
                angle -= 360f;
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x))
                axis = Vector3.zero;
            pilotRotationActive =
                Mathf.Abs(angle) > 0.5f || Mathf.Abs(roll) > 0.01f;
            Vector3 authority =
                AngularAuthority() + rc3Physics.ControlTorqueAuthority;
            Vector3 alphaMax =
                rc3Physics.DirectionalAngularAcceleration(authority);
            Vector3 maxAngularSpeed = new Vector3(
                Mathf.Sqrt(Mathf.Max(
                    0f,
                    2f * alphaMax.x * 45f * Mathf.Deg2Rad)),
                Mathf.Sqrt(Mathf.Max(
                    0f,
                    2f * alphaMax.y * 45f * Mathf.Deg2Rad)),
                Mathf.Sqrt(Mathf.Max(
                    0f,
                    2f * alphaMax.z * 45f * Mathf.Deg2Rad)));
            Vector3 localAxis =
                transform.InverseTransformDirection(axis).normalized;
            float trackingGain = coreAssistMode ==
                                 VehicleCoreAssistMode.Training
                ? 5.2f
                : 3f;
            Vector3 targetAngularVelocity =
                localAxis * angle * Mathf.Deg2Rad * trackingGain;
            targetAngularVelocity = ClampAxes(
                targetAngularVelocity,
                maxAngularSpeed);
            Vector3 currentAngularVelocity =
                transform.InverseTransformDirection(body.angularVelocity);
            Vector3 feedback =
                coreAssistMode == VehicleCoreAssistMode.Disabled
                    ? Vector3.zero
                    : currentAngularVelocity;
            float angularResponse =
                coreAssistMode == VehicleCoreAssistMode.Training
                    ? braking ? 0.08f : 0.10f
                    : braking ? 0.14f : 0.25f;
            Vector3 requestedAlpha =
                (targetAngularVelocity - feedback) /
                angularResponse;
            requestedAlpha = ClampAxes(requestedAlpha, alphaMax);
            Vector3 requestedTorque =
                rc3Physics.TorqueForAngularAcceleration(requestedAlpha);
            return transform.TransformDirection(
                ClampAxes(requestedTorque, authority));
        }

        void UpdateTrimTorque(Vector3 totalAuthority)
        {
            if (coreAssistMode == VehicleCoreAssistMode.Disabled)
            {
                trimTorqueLocal = Vector3.zero;
                rc3Physics.SetAssistTelemetry(Vector3.zero, totalAuthority);
                return;
            }
            VehiclePhysicsSnapshot snapshot = rc3Physics.Snapshot;
            bool frozen = pilotRotationActive ||
                          body.angularVelocity.magnitude > 15f * Mathf.Deg2Rad ||
                          Mathf.Abs(snapshot.angleOfAttackDegrees) > 12f ||
                          snapshot.flightEnvelope.separation > 0.25f;
            float budget = coreAssistMode == VehicleCoreAssistMode.Training ? 0.60f : 0.35f;
            Vector3 limit = totalAuthority * budget;
            Vector3 target = ClampAxes(-snapshot.aerodynamicTorqueLocal, limit);
            if (!frozen)
            {
                float rate = Mathf.Max(10f, limit.magnitude) * 0.4f;
                trimTorqueLocal = Vector3.MoveTowards(trimTorqueLocal, target, rate * Time.fixedDeltaTime);
            }
            rc3Physics.SetAssistTelemetry(trimTorqueLocal, totalAuthority);
        }

        void ApplyAerodynamics()
        {
            rc3Physics.AccumulateAerodynamics(
                body,
                transform,
                airDensity,
                airflowField,
                environmentSample,
                ledger);
            VehiclePhysicsSnapshot snapshot = rc3Physics.Snapshot;
            telemetry.currentLift = snapshot.currentLift;
            telemetry.currentDrag = snapshot.currentDrag;
            telemetry.liftCenterLocal = snapshot.liftCenterLocal;
            telemetry.dragCenterLocal = snapshot.dragCenterLocal;
            telemetry.aerodynamicTorqueLocal = snapshot.aerodynamicTorqueLocal;
            telemetry.angleOfAttack = snapshot.angleOfAttackDegrees;
            telemetry.sideslipAngle = snapshot.sideSlipDegrees;
            telemetry.inertiaTriangleValid = snapshot.inertiaTriangleValid;
            telemetry.inertiaFallbackUsed = snapshot.inertiaFallbackUsed;
            telemetry.aeroPanelCount = snapshot.aeroPanelCount;
            telemetry.stalledPanelCount = snapshot.stalledPanelCount;
            telemetry.separation = snapshot.flightEnvelope.separation;
            telemetry.leftWingSeparation = snapshot.leftWingSeparation;
            telemetry.rightWingSeparation = snapshot.rightWingSeparation;
            telemetry.envelopeInputScale = snapshot.flightEnvelope.inputScale;
            telemetry.staticMargin = snapshot.stability.staticMargin;
            telemetry.trimUsage = snapshot.stability.trimUsage;
            telemetry.averageGroundEffect = snapshot.averageGroundEffect;
            telemetry.controlSurfaceAuthority = rc3Physics.ControlTorqueAuthority;
            telemetry.meanWindVelocity = environmentSample.meanWindVelocity;
            telemetry.gustVelocity = environmentSample.gustVelocity;
            telemetry.airDensity = environmentSample.airDensity;
            telemetry.ambientPressure = environmentSample.ambientPressure;
        }

        float CalculateLiftAuthority(Vector3 up)
        {
            float result =
                coreAssistMode == VehicleCoreAssistMode.Training
                    ? trainingUpAssistForce
                    : 0f;
            for (int index = 0; index < airMovers.Count; index++)
            {
                AirMover mover = airMovers[index];
                result += Mathf.Max(
                              0f,
                              Vector3.Dot(WorldDirection(mover), up)) *
                          mover.maximumForce *
                          CachedMoverForceScale(index);
            }
            return result;
        }

        Vector3 AngularAuthority()
        {
            DirectionalAuthority24 authority = rcs24.Authority;
            // Keep one-way authority visible. The signed allocator decides
            // whether the requested direction is actually available.
            return new Vector3(
                Mathf.Max(authority.positiveTorque.x, authority.negativeTorque.x),
                Mathf.Max(authority.positiveTorque.y, authority.negativeTorque.y),
                Mathf.Max(authority.positiveTorque.z, authority.negativeTorque.z));
        }

        Vector3 WorldDirection(AirMover mover)
        {
            return transform.TransformDirection(
                mover.localDirection).normalized;
        }

        float EffectiveThrustFactor(AirMover mover)
        {
            if (!mover.propeller)
            {
                float pressureRatio = Mathf.Clamp01(
                    environmentSample.ambientPressure / 101325f);
                return Mathf.Clamp(1f - 0.06f * pressureRatio, 0.88f, 1f);
            }
            if (!environmentSample.hasAtmosphere || airDensity <= 0.0001f)
                return 0f;

            Vector3 worldPosition =
                transform.TransformPoint(mover.localPosition);
            Vector3 relativeAir =
                EnvironmentRelativeVelocity(worldPosition);
            Vector3 axis = WorldDirection(mover);
            float axial = Vector3.Dot(relativeAir, axis);
            Vector3 lateral = relativeAir - axis * axial;
            float forwardRatio = Mathf.Max(0f, axial) /
                                 Mathf.Max(1f, mover.softSpeed);
            float advanceEfficiency = Mathf.Clamp01(
                1f - forwardRatio * forwardRatio);
            if (axial < 0f)
                advanceEfficiency = Mathf.Min(
                    1.1f,
                    1f + -axial / Mathf.Max(1f, mover.softSpeed) * 0.1f);
            float lateralEfficiency = 1f /
                (1f + lateral.magnitude / 40f);
            mover.intakeEfficiency = rc3Physics.EstimateIntakeVisibility(
                mover.localPosition,
                mover.localDirection,
                mover.runtimeId);
            return Mathf.Clamp(
                airDensity / 1.225f *
                advanceEfficiency *
                lateralEfficiency *
                mover.intakeEfficiency,
                0f,
                1.1f);
        }

        void BuildPropellerWashes()
        {
            propellerWashes.Clear();
            if (!environmentSample.hasAtmosphere || airDensity <= 0.0001f)
                return;
            for (int index = 0; index < airMovers.Count; index++)
            {
                AirMover mover = airMovers[index];
                if (!mover.propeller || mover.actualThrottle <= 0.0001f)
                    continue;
                Vector3 diskCenter =
                    transform.TransformPoint(mover.localPosition);
                Vector3 forceAxis = WorldDirection(mover);
                Vector3 relativeAir =
                    EnvironmentRelativeVelocity(diskCenter);
                float axial = Mathf.Max(0f, Vector3.Dot(relativeAir, forceAxis));
                float area = Mathf.PI * mover.diskRadius * mover.diskRadius;
                float thrust = mover.maximumForce *
                    CachedMoverForceScale(index) *
                    mover.actualThrottle;
                float induced = 0.5f *
                    (-axial + Mathf.Sqrt(
                        axial * axial +
                        2f * thrust /
                        Mathf.Max(0.01f, airDensity * area)));
                propellerWashes.Add(new PropellerWashSource
                {
                    runtimeId = mover.runtimeId,
                    worldDiskCenter = diskCenter,
                    worldAirflowDirection = -forceAxis,
                    diskRadius = mover.diskRadius,
                    inducedVelocity = Mathf.Max(0f, induced),
                    swirlRatio = 0.18f,
                    washLength = Mathf.Max(6f, mover.diskRadius * 12f),
                    occlusionEfficiency = mover.intakeEfficiency,
                    rotationSign = mover.rotationSign
                });
            }
        }

        Vector3 EnvironmentRelativeVelocity(Vector3 worldPosition)
        {
            IPlanetEnvironmentProvider provider =
                environmentProvider ?? PlanetEnvironmentRuntime.Active;
            PlanetEnvironmentSample sample = SampleAirEnvironment(
                provider,
                worldPosition);
            return body.GetPointVelocity(worldPosition) -
                   sample.atmosphereVelocity;
        }

        PlanetEnvironmentSample SampleAirEnvironment(
            IPlanetEnvironmentProvider provider,
            Vector3 worldPosition)
        {
            if (provider == null)
                return environmentSample;
            if (provider is IPlanetAirEnvironmentProvider airProvider)
            {
                return airProvider.SampleAirflow(
                    worldPosition,
                    Time.fixedTimeAsDouble);
            }
            return provider.Sample(
                worldPosition,
                Time.fixedTimeAsDouble);
        }

        void ConfigureAirflow(IPlanetEnvironmentProvider provider)
        {
            airflowField.Configure(
                provider,
                environmentSample,
                propellerWashes,
                (start, end, source, target) =>
                    rc3Physics.IsAirflowPathBlocked(
                        start,
                        end,
                        source,
                        target,
                        transform));
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
            RefreshTelemetry();
        }

        void BuildMassProperties(GridModuleView[] views)
        {
            rc3Physics.Rebuild(
                views,
                body,
                transform,
                massGeometryScale);
            VehicleMassProperties properties = rc3Physics.MassProperties;
            telemetry.totalMass = properties.totalMass;
            telemetry.centerOfMassLocal = properties.centerOfMassLocal;
            telemetry.inertia = properties.inertiaTensor;
        }

        void BuildMovementParts(GridModuleView[] views)
        {
            ZeroAirThrottle();
            airMovers.Clear();
            var currentWheels = new List<ModularWheelRuntime>();
            foreach (GridModuleView view in views)
            {
                if (view == null || !view.gameObject.activeInHierarchy)
                    continue;
                foreach (ModularWheelRuntime wheel in
                         view.GetComponentsInChildren<
                             ModularWheelRuntime>(true))
                {
                    if (wheel != null &&
                        wheel.enabled &&
                        wheel.gameObject.activeInHierarchy &&
                        !currentWheels.Contains(wheel))
                    {
                        currentWheels.Add(wheel);
                    }
                }
                NeoXBehaviorModule module =
                    view.GetComponentInChildren<NeoXBehaviorModule>(true);
                if (module == null)
                    continue;
                if (module.BehaviorKind == GridModuleBehaviorKind.Thruster)
                    AddAirMover(module, view.Record.RuntimeId);
            }
            groundMobility.Rebuild(
                body,
                transform,
                currentWheels);
            RebuildRcs24Allocator();
        }

        void RefreshAerodynamicMetrics()
        {
            VehiclePhysicsSnapshot snapshot = rc3Physics.Snapshot;
            totalWingArea = snapshot.wingArea;
            bodyCdArea = new Vector3(
                Mathf.Max(0.05f, snapshot.dragArea.x),
                Mathf.Max(0.05f, snapshot.dragArea.y),
                Mathf.Max(0.05f, snapshot.dragArea.z));
            telemetry.exposedArea = snapshot.exposedArea;
            telemetry.dragArea = snapshot.dragArea;
            telemetry.wingArea = snapshot.wingArea;
        }

        static Vector3 Divide(Vector3 numerator, Vector3 denominator)
        {
            return new Vector3(
                denominator.x > 0.001f ? numerator.x / denominator.x : 0f,
                denominator.y > 0.001f ? numerator.y / denominator.y : 0f,
                denominator.z > 0.001f ? numerator.z / denominator.z : 0f);
        }

        static Vector3 ClampAxes(Vector3 value, Vector3 limits)
        {
            return new Vector3(
                Mathf.Clamp(value.x, -limits.x, limits.x),
                Mathf.Clamp(value.y, -limits.y, limits.y),
                Mathf.Clamp(value.z, -limits.z, limits.z));
        }

        static float SafeRatio(float value, float maximum)
        {
            return maximum > 0.001f
                ? Mathf.Clamp(value / maximum, -1f, 1f)
                : 0f;
        }

        void AddAirMover(
            NeoXBehaviorModule module,
            string runtimeId)
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
                force = 7500f;
                response = 0.2f;
                speed = 80f;
                atmosphereOnly = true;
                propeller = true;
            }
            else if (id.IndexOf(
                         "rocket_222",
                         StringComparison.OrdinalIgnoreCase) >= 0)
            {
                force = 120000f;
                response = 0.12f;
                speed = 140f;
                atmosphereOnly = false;
                propeller = false;
            }
            else
            {
                Debug.LogWarning(
                    $"RC3.1: 推进器 {id} 缺少人工物理配置，已禁用推力。",
                    module);
                return;
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
                runtimeId = runtimeId,
                localPosition = transform.InverseTransformPoint(
                    module.WorldExhaustPosition),
                localDirection = direction,
                maximumForce = force * actuatorForceMultiplier,
                responseTime = response,
                softSpeed = speed,
                atmosphereOnly = atmosphereOnly,
                propeller = propeller,
                diskRadius = propeller ? 1f : 0.25f,
                rotationSign = StableRotationSign(runtimeId),
                intakeEfficiency = 1f
            });
        }

        static int StableRotationSign(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                if (!string.IsNullOrEmpty(value))
                {
                    for (int index = 0; index < value.Length; index++)
                    {
                        hash ^= value[index];
                        hash *= 16777619u;
                    }
                }
                return (hash & 1u) == 0u ? 1 : -1;
            }
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
            float strongestInstalledPlanarForce = Mathf.Max(
                Mathf.Max(
                    CalculatePhysicalDirectionalForce(Vector3.right),
                    CalculatePhysicalDirectionalForce(Vector3.left)),
                Mathf.Max(
                    CalculatePhysicalDirectionalForce(Vector3.forward),
                    CalculatePhysicalDirectionalForce(Vector3.back)));
            if (!trainingCoreAuthorityOverride)
            {
                trainingUpAssistForce = TrainingUpForce;
                trainingDownAssistForce = TrainingDownForce;
                trainingPlanarAssistForce =
                    TrainingFlightAssist.CalculatePlanarAssistForce(
                        body.mass,
                        strongestInstalledPlanarForce);
            }
            moverForceScales = new float[airMovers.Count];
            moverForceScalesValid = false;
            rcs24.Rebuild(
                inputs,
                body.centerOfMass,
                coreAssistMode == VehicleCoreAssistMode.Training,
                trainingUpAssistForce,
                trainingDownAssistForce,
                trainingPlanarAssistForce);
        }

        float CalculatePhysicalDirectionalForce(Vector3 localDirection)
        {
            if (localDirection.sqrMagnitude < 0.0001f)
                return 0f;
            Vector3 direction = localDirection.normalized;
            float force = 0f;
            for (int index = 0; index < airMovers.Count; index++)
            {
                AirMover mover = airMovers[index];
                float alignment = Vector3.Dot(
                    mover.localDirection,
                    direction);
                if (alignment <= 0.0001f)
                    continue;
                force += mover.maximumForce * alignment;
            }
            return force;
        }

void RefreshTelemetry()
        {
            BuildMoverForceScales();
            RefreshAerodynamicMetrics();
            Vector3 positiveForce = new Vector3(
                CalculateDirectionalForce(transform.right),
                CalculateDirectionalForce(transform.up),
                CalculateDirectionalForce(transform.forward));
            Vector3 negativeForce = new Vector3(
                CalculateDirectionalForce(-transform.right),
                CalculateDirectionalForce(-transform.up),
                CalculateDirectionalForce(-transform.forward));
            Vector3 angularAuthority = AngularAuthority();
            if (coreAssistMode == VehicleCoreAssistMode.Training)
                angularAuthority += Vector3.one * TrainingTorque;
            Vector3 angularAcceleration =
                rc3Physics.DirectionalAngularAcceleration(angularAuthority);
            float forwardForce = positiveForce.z;
            float terminalSpeed = forwardForce <= 0f
                ? 0f
                : Mathf.Sqrt(2f * forwardForce /
                    Mathf.Max(0.001f, airDensity * Mathf.Max(0.05f, bodyCdArea.z)));
            float stallSpeed = totalWingArea <= 0.01f
                ? 0f
                : Mathf.Sqrt(2f * body.mass * gravity.magnitude /
                    Mathf.Max(0.001f, airDensity * totalWingArea * 1.6f));
            telemetry.groundTopSpeed = groundMobility.TopSpeed;
            telemetry.airTopSpeed = terminalSpeed;
            Vector3 environmentUp = gravity.sqrMagnitude > 0.0001f
                ? -gravity.normalized
                : Vector3.up;
            telemetry.hoverRatio = CalculateLiftAuthority(environmentUp) /
                Mathf.Max(1f, body.mass * gravity.magnitude);
            telemetry.positiveForce = positiveForce;
            telemetry.negativeForce = negativeForce;
            telemetry.positiveAcceleration = positiveForce / Mathf.Max(1f, body.mass);
            telemetry.negativeAcceleration = negativeForce / Mathf.Max(1f, body.mass);
            telemetry.angularAcceleration = angularAcceleration;
            telemetry.estimatedAngularSpeed = new Vector3(
                Mathf.Sqrt(Mathf.Max(0f, 2f * angularAcceleration.x * 45f * Mathf.Deg2Rad)),
                Mathf.Sqrt(Mathf.Max(0f, 2f * angularAcceleration.y * 45f * Mathf.Deg2Rad)),
                Mathf.Sqrt(Mathf.Max(0f, 2f * angularAcceleration.z * 45f * Mathf.Deg2Rad)));
            telemetry.stallSpeed = stallSpeed;
            telemetry.zeroToFiftyTime = forwardForce <= 0.01f
                ? 0f
                : 50f * body.mass / Mathf.Max(0.01f, forwardForce * CruiseThrottle);
            telemetry.brakingTime = negativeForce.z <= 0.01f
                ? 0f
                : 50f * body.mass / Mathf.Max(0.01f, negativeForce.z * CruiseThrottle);
            float weakestAngularAuthority = Mathf.Min(
                angularAuthority.x,
                Mathf.Min(angularAuthority.y, angularAuthority.z));
            telemetry.stabilizerUsage = weakestAngularAuthority <= 0.01f
                ? 1f
                : Mathf.Clamp01(CoreDampingTorque /
                    Mathf.Max(1f, weakestAngularAuthority));
            telemetry.activeWheels = groundMobility.WheelCount;
            telemetry.activeAirMovers = airMovers.Count;

            bool fixedWingCapable = totalWingArea > 0.01f &&
                stallSpeed > 0f && terminalSpeed > stallSpeed;
            bool slowTurning = angularAcceleration.x < 0.35f ||
                angularAcceleration.y < 0.35f || angularAcceleration.z < 0.35f;
            if (groundMobility.WheelCount > 0)
            {
                telemetry.status =
                    telemetry.hoverRatio >= 1f ||
                    fixedWingCapable ||
                    airMovers.Count > 0
                        ? "可陆空混合"
                        : "可地面驾驶";
            }
            else if (telemetry.hoverRatio >= 1f)
                telemetry.status = slowTurning ? "可飞但转向迟缓" : "VTOL完整";
            else if (fixedWingCapable)
                telemetry.status = "固定翼可起飞";
            else if (coreAssistMode == VehicleCoreAssistMode.Training)
                telemetry.status = "街机辅助飞行";
            else if (airMovers.Count > 0)
                telemetry.status = "推力不足";
            else
                telemetry.status = "缺少指定控制轴";
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
        }

        void BindStructureGraph(VehicleStructureGraph value)
        {
            if (structureGraph == value)
                return;
            if (structureGraph != null)
                structureGraph.StructureChanged -= QueueStructureRebuild;
            structureGraph = value;
            if (structureGraph != null)
                structureGraph.StructureChanged += QueueStructureRebuild;
        }

        void OnDestroy()
        {
            ResetEvasion();
            groundMobility.SetFlightMode(false);
            evasionPresentation?.SetFlightActive(false);
            if (model != null)
                model.Changed -= HandleModelChanged;
            if (presenter != null)
                presenter.Rebuilt -= HandlePresenterRebuilt;
            if (structureGraph != null)
                structureGraph.StructureChanged -= QueueStructureRebuild;
        }
    }
}
