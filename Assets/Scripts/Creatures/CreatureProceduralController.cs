using System;
using System.Text;
using UnityEngine;

[Serializable]
public struct CreatureMotionCommand
{
    public Vector3 desiredVelocityWorld;
    public Vector3 desiredFacingWorld;
    public bool jumpRequested;
    public bool attackRequested;
    public Vector3 attackTargetWorld;
}

public enum CreatureProceduralActionState
{
    Idle,
    Start,
    Walk,
    Trot,
    Run,
    Stop,
    JumpPrepare,
    Airborne,
    Landing,
    AttackWindup,
    AttackStrike,
    AttackRecover
}

public struct CreatureAttackImpact
{
    public Vector3 position;
    public Vector3 direction;
    public float impulse;
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public sealed class CreatureProceduralController : MonoBehaviour
{
    sealed class LegRuntime
    {
        public CreaturePhenotypeLeg definition;
        public Transform upper;
        public Transform lower;
        public Transform distal;
        public Transform foot;
        public Vector3 plantedPosition;
        public Vector3 swingStart;
        public Vector3 swingTarget;
        public Vector3 groundNormal;
        public Vector3 plantedNormal;
        public Vector3 swingTargetNormal;
        public bool initialized;
        public bool stance;
        public bool previousStance;
        public bool forcedPlantActive;
        public float forcedPlantStartedAt;
        public Vector3 forcedPlantStart;
        public Vector3 forcedPlantStartNormal;
        public float phase;
        public float reach01;
        public float supportLoad01;
    }

    readonly RaycastHit[] groundHits = new RaycastHit[16];
    LegRuntime[] legs = Array.Empty<LegRuntime>();
    Vector2[] supportPoints = Array.Empty<Vector2>();
    SphericalGravitySource gravitySource;
    Rigidbody body;
    CapsuleCollider capsule;
    CreatureGenome genome;
    CreaturePhenotype phenotype;
    CreatureRig rig;
    LayerMask groundLayers;
    CreatureMotionCommand command;
    Vector3 bodyRestLocalPosition;
    Quaternion bodyRestLocalRotation;
    Quaternion neckRestRotation;
    Quaternion headRestRotation;
    Quaternion tailRestRotation;
    Vector3 previousTangentialVelocity;
    Vector3 surfaceForward = Vector3.forward;
    Vector3 groundNormal = Vector3.up;
    float gaitPhase;
    float stateTime;
    float commandAge;
    float planningAccumulator;
    float planningInterval;
    float stabilityConfidence;
    bool hasCommand;
    bool pendingJump;
    bool pendingAttack;
    bool attackImpactSent;
    bool jumpImpulseSent;
    Camera lodCamera;

    public CreatureProceduralActionState CurrentState { get; private set; }
    public Vector3 DesiredVelocity { get; private set; }
    public Vector3 GroundNormal => groundNormal;
    public Vector3 SurfaceForward => surfaceForward;
    public bool IsGrounded { get; private set; }
    public bool IsStable { get; private set; }
    public int ContactFootCount { get; private set; }
    public float CurrentSpeed { get; private set; }
    public CreaturePhenotype Phenotype => phenotype;
    public event Action<CreatureAttackImpact> OnAttackImpact;

    public void Configure(
        SphericalGravitySource source,
        Rigidbody targetBody,
        CapsuleCollider targetCapsule,
        CreatureGenome sourceGenome,
        CreaturePhenotype sourcePhenotype,
        CreatureRig sourceRig,
        LayerMask layers)
    {
        gravitySource = source;
        body = targetBody;
        capsule = targetCapsule;
        genome = sourceGenome;
        rig = sourceRig;
        groundLayers = layers;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        Reconfigure(sourcePhenotype, true);
        if (sourceGenome != null)
        {
            BiotaScannable.Ensure(
                gameObject,
                BiotaDiscoveryType.Creature,
                $"procedural:{sourceGenome.generatorVersion}:{sourceGenome.seed}",
                $"{sourceGenome.topology} 生物",
                $"程序化生物 · {sourceGenome.locomotionArchetype}");
        }
    }

    public void Reconfigure(CreaturePhenotype sourcePhenotype, bool resetMotion = false)
    {
        phenotype = sourcePhenotype ?? throw new ArgumentNullException(nameof(sourcePhenotype));
        body.mass = phenotype.mass;
        body.centerOfMass = phenotype.centerOfMass;
        bodyRestLocalPosition = rig.body.localPosition;
        bodyRestLocalRotation = rig.body.localRotation;
        neckRestRotation = rig.neck.localRotation;
        headRestRotation = rig.head.localRotation;
        tailRestRotation = rig.tailBase.localRotation;
        BuildLegRuntime();
        ResetFootContacts();
        if (resetMotion)
        {
            gaitPhase = 0f;
            stateTime = 0f;
            CurrentState = CreatureProceduralActionState.Idle;
            previousTangentialVelocity = Vector3.zero;
        }
    }

    public void SetCommand(CreatureMotionCommand value)
    {
        pendingJump |= value.jumpRequested && (phenotype == null || phenotype.allowsAerialGait);
        pendingAttack |= value.attackRequested;
        command = value;
        commandAge = 0f;
        hasCommand = true;
    }

    public void ResetFootContacts()
    {
        for (int i = 0; i < legs.Length; i++)
        {
            legs[i].initialized = false;
            legs[i].stance = true;
            legs[i].previousStance = true;
            legs[i].reach01 = 0f;
        }
        ContactFootCount = 0;
        stabilityConfidence = 0f;
        IsStable = false;
    }

    void FixedUpdate()
    {
        if (gravitySource == null || body == null || phenotype == null || rig == null)
            return;

        float deltaTime = Mathf.Max(0.001f, Time.fixedDeltaTime);
        commandAge += deltaTime;
        if (commandAge > 0.25f)
        {
            hasCommand = false;
            command = default;
        }

        Vector3 up = gravitySource.GetUp(body.position);
        Vector3 gravity = gravitySource.GetGravity(body.position);
        body.AddForce(gravity, ForceMode.Acceleration);
        IsGrounded = CheckGround(up, out RaycastHit bodyGround);
        if (IsGrounded)
            groundNormal = Vector3.Slerp(groundNormal, bodyGround.normal, 0.35f).normalized;
        else
            groundNormal = up;

        Vector3 requestedVelocity = hasCommand
            ? Vector3.ProjectOnPlane(command.desiredVelocityWorld, up)
            : Vector3.zero;
        requestedVelocity = Vector3.ClampMagnitude(requestedVelocity, phenotype.runSpeed);
        DesiredVelocity = requestedVelocity;
        Vector3 tangentialVelocity = Vector3.ProjectOnPlane(body.velocity, up);
        CurrentSpeed = tangentialVelocity.magnitude;

        UpdateActionState(deltaTime, up, gravity.magnitude);
        UpdateHeading(up, requestedVelocity, deltaTime);
        ApplyGroundMovement(tangentialVelocity, deltaTime);
        previousTangentialVelocity = tangentialVelocity;

        if (IsGrounded && CurrentState != CreatureProceduralActionState.Airborne)
        {
            ApplyLegSupport(up, bodyGround, gravity.magnitude);
        }
    }

    void ApplyLegSupport(Vector3 up, RaycastHit groundHit, float gravityMagnitude)
    {
        float currentHeight = Vector3.Dot(body.position - groundHit.point, up);
        float targetHeight = phenotype.bodyClearance;
        if (CurrentState == CreatureProceduralActionState.JumpPrepare)
            targetHeight -= phenotype.stepHeight * 0.45f;
        else if (CurrentState == CreatureProceduralActionState.Landing)
            targetHeight -= phenotype.stepHeight * 0.18f
                * (1f - Mathf.Clamp01(stateTime / 0.28f));

        float heightError = targetHeight - currentHeight;
        float verticalSpeed = Vector3.Dot(body.velocity, up);
        float contactFactor = Mathf.Lerp(0.62f, 1f,
            Mathf.Clamp01(ContactFootCount / Mathf.Max(1f, legs.Length)));
        float supportAcceleration = gravityMagnitude
            + heightError * 32f * contactFactor
            - verticalSpeed * 9f;
        body.AddForce(up * Mathf.Clamp(supportAcceleration, 0f, 46f), ForceMode.Acceleration);
    }

    void LateUpdate()
    {
        if (gravitySource == null || phenotype == null || rig == null || legs.Length == 0)
            return;

        UpdatePlanningLod();
        planningAccumulator += Time.deltaTime;
        bool refreshGroundPlan = planningInterval <= 0f || planningAccumulator >= planningInterval;
        if (refreshGroundPlan) planningAccumulator = 0f;

        Vector3 up = gravitySource.GetUp(body.position);
        Vector3 forward = Vector3.ProjectOnPlane(surfaceForward, up).normalized;
        Vector3 right = Vector3.Cross(up, forward).normalized;
        float speed01 = Mathf.Clamp01(DesiredVelocity.magnitude / Mathf.Max(0.01f, phenotype.runSpeed));
        bool forcePlantedPose = RequiresPlantedPose();
        float frequency = phenotype.baseGaitFrequency * Mathf.Lerp(0.65f, 1.85f, speed01);
        if (forcePlantedPose)
            frequency = 0f;
        gaitPhase = Mathf.Repeat(gaitPhase + frequency * Time.deltaTime, 1f);

        ApplyBodyPose(up, speed01);
        ContactFootCount = 0;
        float maximumReach = 0f;
        for (int i = 0; i < legs.Length; i++)
        {
            UpdateLeg(legs[i], up, forward, right, speed01, refreshGroundPlan, forcePlantedPose);
            if (legs[i].stance && legs[i].initialized) ContactFootCount++;
            maximumReach = Mathf.Max(maximumReach, legs[i].reach01);
        }
        UpdateSupportLoads();
        bool fullPlantStable = forcePlantedPose
            && ContactFootCount == legs.Length
            && maximumReach < 0.995f;
        bool rawStable = fullPlantStable
            || (ContactFootCount >= 2
                && maximumReach < 0.995f
                && EvaluateSupportPolygon(right, forward));
        stabilityConfidence = Mathf.MoveTowards(
            stabilityConfidence, rawStable ? 1f : 0f, Time.deltaTime * (rawStable ? 7f : 4f));
        IsStable = stabilityConfidence >= 0.35f;
    }

    void UpdateActionState(float deltaTime, Vector3 up, float gravityMagnitude)
    {
        stateTime += deltaTime;
        bool groundedState = CurrentState != CreatureProceduralActionState.Airborne;
        if (groundedState && pendingAttack && IsGrounded
            && (int)CurrentState < (int)CreatureProceduralActionState.JumpPrepare)
        {
            pendingAttack = false;
            pendingJump = false;
            Vector3 attackDirection = Vector3.ProjectOnPlane(command.attackTargetWorld - body.position, up);
            if (attackDirection.sqrMagnitude > 0.0001f)
                surfaceForward = attackDirection.normalized;
            EnterState(CreatureProceduralActionState.AttackWindup);
            attackImpactSent = false;
        }
        else if (groundedState && pendingJump && IsGrounded
            && (int)CurrentState < (int)CreatureProceduralActionState.JumpPrepare)
        {
            pendingJump = false;
            pendingAttack = false;
            EnterState(CreatureProceduralActionState.JumpPrepare);
            jumpImpulseSent = false;
        }

        switch (CurrentState)
        {
            case CreatureProceduralActionState.JumpPrepare:
                DesiredVelocity = Vector3.zero;
                bool jumpSupportReady = ContactFootCount == legs.Length && IsStable;
                if (stateTime >= 0.28f && (jumpSupportReady || stateTime >= 0.52f) && !jumpImpulseSent)
                {
                    float jumpHeight = Mathf.Clamp(phenotype.bodyClearance * 0.38f, 0.75f, 1.8f);
                    float jumpSpeed = Mathf.Sqrt(2f * Mathf.Max(0.1f, gravityMagnitude) * jumpHeight);
                    body.AddForce(up * jumpSpeed, ForceMode.VelocityChange);
                    jumpImpulseSent = true;
                    EnterState(CreatureProceduralActionState.Airborne);
                }
                return;
            case CreatureProceduralActionState.Airborne:
                if (IsGrounded && stateTime > 0.16f && Vector3.Dot(body.velocity, up) <= 0.5f)
                {
                    ResetFootContacts();
                    EnterState(CreatureProceduralActionState.Landing);
                }
                return;
            case CreatureProceduralActionState.Landing:
                if (stateTime >= 0.28f)
                {
                    EnterLocomotionState();
                }
                return;
            case CreatureProceduralActionState.AttackWindup:
                DesiredVelocity = Vector3.zero;
                if (stateTime >= 0.22f)
                    EnterState(CreatureProceduralActionState.AttackStrike);
                return;
            case CreatureProceduralActionState.AttackStrike:
                if (!attackImpactSent && stateTime >= 0.08f)
                {
                    float impulse = phenotype.runSpeed * 0.42f;
                    body.AddForce(surfaceForward * impulse, ForceMode.VelocityChange);
                    attackImpactSent = true;
                    OnAttackImpact?.Invoke(new CreatureAttackImpact
                    {
                        position = rig.head.position,
                        direction = surfaceForward,
                        impulse = impulse * body.mass
                    });
                }
                if (stateTime >= 0.22f)
                    EnterState(CreatureProceduralActionState.AttackRecover);
                return;
            case CreatureProceduralActionState.AttackRecover:
                DesiredVelocity = Vector3.zero;
                if (stateTime >= 0.32f)
                    EnterLocomotionState();
                return;
        }

        EnterLocomotionState();
    }

    void EnterLocomotionState()
    {
        float requestedSpeed = DesiredVelocity.magnitude;
        CreatureProceduralActionState target;
        if (requestedSpeed < 0.08f)
            target = CurrentSpeed > 0.25f ? CreatureProceduralActionState.Stop : CreatureProceduralActionState.Idle;
        else if (CurrentState == CreatureProceduralActionState.Idle || CurrentState == CreatureProceduralActionState.Stop)
            target = CreatureProceduralActionState.Start;
        else if (phenotype.locomotionArchetype == CreatureLocomotionArchetype.GraviportalElephant)
            target = CreatureProceduralActionState.Walk;
        else if (requestedSpeed < phenotype.walkSpeed * 1.05f)
            target = CreatureProceduralActionState.Walk;
        else if (requestedSpeed < phenotype.trotSpeed * 1.08f)
            target = CreatureProceduralActionState.Trot;
        else
            target = CreatureProceduralActionState.Run;

        if (target == CreatureProceduralActionState.Start && stateTime > 0.18f)
            target = phenotype.locomotionArchetype == CreatureLocomotionArchetype.GraviportalElephant
                || requestedSpeed < phenotype.walkSpeed * 1.05f
                ? CreatureProceduralActionState.Walk : CreatureProceduralActionState.Trot;
        if (target == CreatureProceduralActionState.Stop && CurrentSpeed < 0.2f)
            target = CreatureProceduralActionState.Idle;
        if (target != CurrentState) EnterState(target);
    }

    void EnterState(CreatureProceduralActionState state)
    {
        if (CurrentState == state) return;
        CurrentState = state;
        stateTime = 0f;
    }

    void UpdateHeading(Vector3 up, Vector3 requestedVelocity, float deltaTime)
    {
        Vector3 requestedFacing = hasCommand
            ? Vector3.ProjectOnPlane(command.desiredFacingWorld, up)
            : Vector3.zero;
        if (requestedFacing.sqrMagnitude < 0.0001f)
            requestedFacing = requestedVelocity;
        if (requestedFacing.sqrMagnitude > 0.0001f)
            surfaceForward = Vector3.Slerp(surfaceForward, requestedFacing.normalized,
                1f - Mathf.Exp(-8f * deltaTime)).normalized;
        surfaceForward = Vector3.ProjectOnPlane(surfaceForward, up).normalized;
        if (surfaceForward.sqrMagnitude < 0.0001f)
            surfaceForward = Vector3.Cross(Vector3.forward, up).normalized;
        body.MoveRotation(Quaternion.LookRotation(surfaceForward, up));
    }

    void ApplyGroundMovement(Vector3 tangentialVelocity, float deltaTime)
    {
        if (CurrentState == CreatureProceduralActionState.Airborne
            || CurrentState == CreatureProceduralActionState.JumpPrepare
            || CurrentState == CreatureProceduralActionState.AttackWindup
            || CurrentState == CreatureProceduralActionState.AttackRecover)
            return;

        float requiredContacts = phenotype.locomotionArchetype == CreatureLocomotionArchetype.HexapodTripod
            ? 3f : 2f;
        float supportFactor = legs.Length == 0 ? 1f : Mathf.Clamp01(ContactFootCount / requiredContacts);
        if (!IsStable) supportFactor *= 0.45f;
        if (!IsGrounded) supportFactor *= 0.12f;
        Vector3 velocityError = DesiredVelocity - tangentialVelocity;
        Vector3 measuredAcceleration = (tangentialVelocity - previousTangentialVelocity) / deltaTime;
        Vector3 acceleration = velocityError * 7.5f - measuredAcceleration * 0.16f;
        float maximumAcceleration = Mathf.Lerp(5f, 24f, supportFactor);
        body.AddForce(Vector3.ClampMagnitude(acceleration, maximumAcceleration), ForceMode.Acceleration);
    }

    void ApplyBodyPose(Vector3 up, float speed01)
    {
        float locomotionBob = Mathf.Sin(gaitPhase * Mathf.PI * 4f) * phenotype.stepHeight * 0.08f * speed01;
        float idleBreath = Mathf.Sin(Time.time * 1.25f) * 0.022f;
        float crouch = 0f;
        float pitch = 0f;
        if (CurrentState == CreatureProceduralActionState.JumpPrepare)
            crouch = -phenotype.stepHeight * Mathf.SmoothStep(0f, 0.72f, stateTime / 0.32f);
        else if (CurrentState == CreatureProceduralActionState.Landing)
            crouch = -phenotype.stepHeight * 0.45f * (1f - Mathf.Clamp01(stateTime / 0.28f));
        else if (CurrentState == CreatureProceduralActionState.AttackWindup)
            pitch = -8f * Mathf.Clamp01(stateTime / 0.22f);
        else if (CurrentState == CreatureProceduralActionState.AttackStrike)
            pitch = 13f * Mathf.Sin(Mathf.Clamp01(stateTime / 0.22f) * Mathf.PI);

        rig.body.localPosition = bodyRestLocalPosition + Vector3.up * (locomotionBob + idleBreath + crouch);
        rig.body.localRotation = bodyRestLocalRotation * Quaternion.Euler(pitch, 0f,
            Mathf.Sin(gaitPhase * Mathf.PI * 2f) * 2.2f * speed01);
        bool fantasyHexapod = phenotype.locomotionArchetype == CreatureLocomotionArchetype.HexapodTripod;
        float headCounter = -locomotionBob * 12f + pitch * -0.45f;
        float scan = fantasyHexapod ? Mathf.Sin(Time.time * 0.47f + phenotype.seed * 0.17f) * 9f : 0f;
        rig.neck.localRotation = neckRestRotation * Quaternion.Euler(headCounter, scan * 0.35f, 0f);
        rig.head.localRotation = headRestRotation * Quaternion.Euler(-headCounter * 0.55f, scan, 0f);
        rig.tailBase.localRotation = tailRestRotation * Quaternion.Euler(
            fantasyHexapod ? Mathf.Sin(Time.time * 0.72f) * 3f : 0f,
            Mathf.Sin(gaitPhase * Mathf.PI * 2f + 0.7f) * Mathf.Lerp(4f, 14f, speed01), 0f);
    }

    void UpdateLeg(
        LegRuntime leg,
        Vector3 up,
        Vector3 forward,
        Vector3 right,
        float speed01,
        bool refreshGroundPlan,
        bool forcePlantedPose)
    {
        float walkOffset = GetWalkOffset(leg.definition);
        float offset;
        if (phenotype.locomotionArchetype == CreatureLocomotionArchetype.GraviportalElephant)
        {
            // Elephants retain a lateral-sequence footfall pattern even at their fastest gait.
            offset = walkOffset;
        }
        else
        {
            float trotOffset = GetTrotOffset(leg.definition);
            float gallopOffset = GetGallopOffset(leg.definition);
            float walkToTrot = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                phenotype.walkSpeed * 0.72f, phenotype.trotSpeed, DesiredVelocity.magnitude));
            float trotToGallop = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                phenotype.trotSpeed * 0.92f, phenotype.runSpeed, DesiredVelocity.magnitude));
            offset = Mathf.Lerp(Mathf.Lerp(walkOffset, trotOffset, walkToTrot), gallopOffset, trotToGallop);
        }
        leg.phase = Mathf.Repeat(gaitPhase + offset, 1f);
        float dutyFactor = CalculateDutyFactor(speed01);
        bool forceAirPose = CurrentState == CreatureProceduralActionState.Airborne;
        bool nextStance = !forceAirPose && leg.phase < dutyFactor;
        bool enteredSwing = !nextStance && leg.previousStance;
        bool enteredStance = nextStance && !leg.previousStance;
        leg.stance = nextStance;

        Vector3 hip = leg.upper.position;
        if (!leg.initialized)
        {
            Vector3 neutral = CalculateStepTarget(leg, hip, up, forward, right, false);
            leg.plantedPosition = SampleGround(neutral, up, out leg.plantedNormal);
            leg.plantedPosition += leg.plantedNormal * leg.definition.footSoleOffset;
            leg.groundNormal = leg.plantedNormal;
            leg.swingStart = leg.swingTarget = leg.plantedPosition;
            leg.swingTargetNormal = leg.plantedNormal;
            leg.initialized = true;
        }

        if (forcePlantedPose)
        {
            UpdateForcedPlant(leg, hip, up, forward, right);
            leg.previousStance = true;
            SolveBiologicalLegGoal(leg, hip, leg.plantedPosition, forward, up);
            return;
        }
        leg.forcedPlantActive = false;

        if (enteredSwing)
        {
            leg.swingStart = leg.plantedPosition;
            Vector3 target = CalculateStepTarget(leg, hip, up, forward, right, true);
            if (refreshGroundPlan)
            {
                leg.swingTarget = SampleGround(target, up, out leg.swingTargetNormal);
                leg.swingTarget += leg.swingTargetNormal * leg.definition.footSoleOffset;
            }
            else
            {
                leg.swingTarget = target;
                leg.swingTargetNormal = leg.plantedNormal;
            }
        }

        if (enteredStance)
        {
            leg.plantedPosition = leg.swingTarget;
            leg.plantedNormal = leg.swingTargetNormal;
            leg.groundNormal = leg.plantedNormal;
        }

        Vector3 footGoal;
        if (forceAirPose)
        {
            footGoal = hip - up * leg.definition.TotalLength * 0.58f
                + forward * (leg.definition.IsFront ? 0.16f : -0.12f);
        }
        else if (!leg.stance)
        {
            float swingT = Mathf.InverseLerp(dutyFactor, 1f, leg.phase);
            float smoothT = swingT * swingT * (3f - 2f * swingT);
            Vector3 linear = Vector3.LerpUnclamped(leg.swingStart, leg.swingTarget, smoothT);
            footGoal = linear + up * (Mathf.Sin(smoothT * Mathf.PI) * phenotype.stepHeight);
            leg.groundNormal = Vector3.Slerp(leg.plantedNormal, leg.swingTargetNormal, smoothT).normalized;
        }
        else
        {
            footGoal = leg.plantedPosition;
            leg.groundNormal = leg.plantedNormal;
        }

        leg.previousStance = leg.stance;
        SolveBiologicalLegGoal(leg, hip, footGoal, forward, up);
    }

    void UpdateForcedPlant(
        LegRuntime leg,
        Vector3 hip,
        Vector3 up,
        Vector3 forward,
        Vector3 right)
    {
        float duration = CurrentState == CreatureProceduralActionState.JumpPrepare ? 0.2f : 0.24f;
        bool completedPreviousPlant = leg.forcedPlantActive
            && Time.time - leg.forcedPlantStartedAt >= duration;
        if (completedPreviousPlant && leg.reach01 > 0.97f)
            leg.forcedPlantActive = false;

        if (!leg.forcedPlantActive)
        {
            leg.forcedPlantActive = true;
            leg.forcedPlantStartedAt = Time.time;
            leg.forcedPlantStart = leg.foot.position;
            leg.forcedPlantStartNormal = leg.groundNormal.sqrMagnitude > 0.001f
                ? leg.groundNormal : up;
            Vector3 neutral = CalculateStepTarget(leg, hip, up, forward, right, false);
            leg.swingTarget = SampleGround(neutral, up, out leg.swingTargetNormal);
            leg.swingTarget += leg.swingTargetNormal * leg.definition.footSoleOffset;
        }

        float t = Mathf.Clamp01((Time.time - leg.forcedPlantStartedAt) / duration);
        float smoothT = t * t * (3f - 2f * t);
        Vector3 linear = Vector3.LerpUnclamped(leg.forcedPlantStart, leg.swingTarget, smoothT);
        float lift = Mathf.Sin(smoothT * Mathf.PI) * phenotype.stepHeight * 0.22f;
        leg.plantedPosition = linear + up * lift;
        leg.plantedNormal = Vector3.Slerp(
            leg.forcedPlantStartNormal, leg.swingTargetNormal, smoothT).normalized;
        leg.groundNormal = leg.plantedNormal;
        leg.stance = true;
        if (t >= 1f)
            leg.plantedPosition = leg.swingTarget;
    }

    Vector3 CalculateStepTarget(
        LegRuntime leg,
        Vector3 hip,
        Vector3 up,
        Vector3 forward,
        Vector3 right,
        bool includeVelocityPrediction)
    {
        float side = leg.definition.IsLeft ? -1f : 1f;
        bool anatomicalUngulate = genome.generatorVersion >= CreatureGenerationVersions.AnatomicalV5
            && phenotype.locomotionArchetype == CreatureLocomotionArchetype.CursorialUngulate;
        bool fantasyHexapod = phenotype.locomotionArchetype == CreatureLocomotionArchetype.HexapodTripod;
        float stanceWidth = phenotype.locomotionArchetype == CreatureLocomotionArchetype.GraviportalElephant
            ? genome.bodyWidth * 0.075f
            : fantasyHexapod ? genome.bodyWidth * 0.2f
            : genome.bodyWidth * (anatomicalUngulate ? 0.06f : 0.035f);
        Vector3 lateral = right * side * stanceWidth;
        Vector3 velocityPrediction = includeVelocityPrediction
            ? Vector3.ClampMagnitude(
                DesiredVelocity * Mathf.Lerp(0.1f, 0.2f,
                    Mathf.Clamp01(DesiredVelocity.magnitude / Mathf.Max(0.01f, phenotype.runSpeed))),
                phenotype.strideLength * 0.58f)
            : Vector3.zero;
        float longitudinalScale = includeVelocityPrediction ? 0.12f : 0.055f;
        float pairStagger = anatomicalUngulate && !includeVelocityPrediction
            ? side * (leg.definition.IsFront ? 0.035f : -0.035f)
            : 0f;
        float pairPosition = fantasyHexapod
            ? (leg.definition.longitudinalPosition - 0.5f) * 1.7f
            : (leg.definition.IsFront ? 1f : -0.65f);
        Vector3 longitudinal = forward * phenotype.strideLength
            * (longitudinalScale * pairPosition + pairStagger);
        // Keep the foot below its own shoulder/hip. A 0.78 extension forced every idle leg
        // into a deep crouch and made the fore and hind chains fold through each other.
        return hip - up * leg.definition.TotalLength * phenotype.neutralLegExtension
            + lateral + velocityPrediction + longitudinal;
    }

    bool RequiresPlantedPose()
    {
        return CurrentState == CreatureProceduralActionState.Idle
            || CurrentState == CreatureProceduralActionState.JumpPrepare
            || CurrentState == CreatureProceduralActionState.Landing
            || CurrentState == CreatureProceduralActionState.AttackWindup
            || CurrentState == CreatureProceduralActionState.AttackRecover
            || (CurrentState == CreatureProceduralActionState.Stop && CurrentSpeed < 0.45f);
    }

    Vector3 SampleGround(Vector3 candidate, Vector3 up, out Vector3 normal)
    {
        Vector3 origin = candidate + up * Mathf.Max(1.2f, phenotype.bodyClearance * 0.55f);
        float radius = Mathf.Clamp(genome.footScale * 0.16f, 0.04f, 0.18f);
        int count = Physics.SphereCastNonAlloc(
            origin, radius, -up, groundHits,
            phenotype.bodyClearance + 3f, groundLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        Vector3 result = gravitySource.GetSurfacePoint(candidate - gravitySource.Center);
        normal = gravitySource.GetUp(result);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider == null || hit.collider.attachedRigidbody == body || hit.distance >= nearest)
                continue;
            if (Vector3.Angle(hit.normal, up) > 40f)
                continue;
            nearest = hit.distance;
            result = hit.point;
            normal = hit.normal;
        }
        return result;
    }

    void SolveBiologicalLegGoal(LegRuntime leg, Vector3 hip, Vector3 requestedFoot, Vector3 forward, Vector3 up)
    {
        Vector3 toRequestedFoot = requestedFoot - hip;
        float requestedFootDistance = Mathf.Max(0.001f, toRequestedFoot.magnitude);
        leg.reach01 = requestedFootDistance / Mathf.Max(0.001f, leg.definition.TotalLength);

        // The hock/wrist sits above and behind the paw. Separating this distal segment is
        // what prevents a digitigrade hind leg from collapsing into one pointed two-bone chain.
        float forwardFraction = Mathf.Clamp(leg.definition.distalForwardFraction, 0f, 0.75f);
        float verticalFraction = Mathf.Sqrt(Mathf.Max(0.01f, 1f - forwardFraction * forwardFraction));
        Vector3 hockTarget = requestedFoot
            + up * (leg.definition.distalLength * verticalFraction)
            - forward * (leg.definition.distalLength * forwardFraction);
        Vector3 toHock = hockTarget - hip;
        float requestedDistance = Mathf.Max(0.001f, toHock.magnitude);
        Vector3 direction = toHock / requestedDistance;
        float upperLength = leg.definition.upperLength;
        float lowerLength = leg.definition.lowerLength;
        float minimum = JointDistance(upperLength, lowerLength, leg.definition.minimumJointAngle);
        float maximum = JointDistance(upperLength, lowerLength, leg.definition.maximumJointAngle);
        float distance = Mathf.Clamp(requestedDistance, minimum, maximum);
        Vector3 solvedHock = hip + direction * distance;

        float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance)
            / (2f * distance);
        float bendHeight = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
        bool fantasyHexapod = phenotype.locomotionArchetype == CreatureLocomotionArchetype.HexapodTripod;
        float bendSign = Mathf.Abs(leg.definition.bendHintLocal.z) > 0.001f
            ? Mathf.Sign(leg.definition.bendHintLocal.z)
            : (leg.definition.IsFront ? -1f : 1f);
        Vector3 bendDirection;
        if (fantasyHexapod)
        {
            Vector3 right = Vector3.Cross(up, forward).normalized;
            float side = leg.definition.IsLeft ? -1f : 1f;
            bendDirection = (right * side * 0.9f + forward * bendSign * 0.35f).normalized;
        }
        else bendDirection = forward * bendSign;
        Vector3 bendHint = Vector3.ProjectOnPlane(bendDirection, direction);
        if (bendHint.sqrMagnitude < 0.0001f)
            bendHint = Vector3.ProjectOnPlane(up, direction);
        bendHint.Normalize();
        Vector3 knee = hip + direction * along + bendHint * bendHeight;

        SetBoneDownAxis(leg.upper, knee - hip, bendHint);
        SetBoneDownAxis(leg.lower, solvedHock - leg.lower.position, bendHint);
        SetBoneDownAxis(leg.distal, requestedFoot - leg.distal.position, -bendHint);
        Vector3 footUp = leg.groundNormal.sqrMagnitude > 0.001f ? leg.groundNormal.normalized : up;
        Vector3 footForward = Vector3.ProjectOnPlane(forward, footUp).normalized;
        if (footForward.sqrMagnitude > 0.0001f)
            leg.foot.rotation = Quaternion.LookRotation(footForward, footUp);
    }

    void UpdateSupportLoads()
    {
        if (phenotype.locomotionArchetype == CreatureLocomotionArchetype.HexapodTripod)
        {
            int contacts = 0;
            for (int i = 0; i < legs.Length; i++)
            {
                legs[i].supportLoad01 = 0f;
                if (legs[i].stance && legs[i].initialized) contacts++;
            }
            float load = contacts > 0 ? 1f / contacts : 0f;
            for (int i = 0; i < legs.Length; i++)
                if (legs[i].stance && legs[i].initialized) legs[i].supportLoad01 = load;
            return;
        }
        int frontContacts = 0;
        int rearContacts = 0;
        for (int i = 0; i < legs.Length; i++)
        {
            legs[i].supportLoad01 = 0f;
            if (!legs[i].stance || !legs[i].initialized) continue;
            if (legs[i].definition.IsFront) frontContacts++;
            else rearContacts++;
        }

        float frontShare = frontContacts > 0 ? phenotype.frontLoadFraction : 0f;
        float rearShare = rearContacts > 0 ? 1f - phenotype.frontLoadFraction : 0f;
        float available = frontShare + rearShare;
        if (available <= 0f) return;
        frontShare /= available;
        rearShare /= available;
        for (int i = 0; i < legs.Length; i++)
        {
            if (!legs[i].stance || !legs[i].initialized) continue;
            legs[i].supportLoad01 = legs[i].definition.IsFront
                ? frontShare / Mathf.Max(1, frontContacts)
                : rearShare / Mathf.Max(1, rearContacts);
        }
    }

    static float JointDistance(float upper, float lower, float jointAngleDegrees)
    {
        float radians = jointAngleDegrees * Mathf.Deg2Rad;
        return Mathf.Sqrt(Mathf.Max(0.0001f,
            upper * upper + lower * lower - 2f * upper * lower * Mathf.Cos(radians)));
    }

    static void SetBoneDownAxis(Transform bone, Vector3 downDirection, Vector3 bendReference)
    {
        if (downDirection.sqrMagnitude < 0.0001f) return;
        Vector3 down = downDirection.normalized;
        Vector3 reference = Vector3.ProjectOnPlane(bendReference, down);
        if (reference.sqrMagnitude < 0.0001f)
            reference = Vector3.ProjectOnPlane(Vector3.forward, down);
        bone.rotation = Quaternion.LookRotation(reference.normalized, -down);
    }

    bool CheckGround(Vector3 up, out RaycastHit nearestHit)
    {
        float radius = Mathf.Max(0.08f, capsule.radius * 0.68f);
        int count = Physics.SphereCastNonAlloc(
            body.position + up * 0.3f, radius, -up, groundHits,
            phenotype.bodyClearance + 1.2f, groundLayers, QueryTriggerInteraction.Ignore);
        nearestHit = default;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider == null || hit.collider.attachedRigidbody == body || hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            nearestHit = hit;
        }
        return nearest <= phenotype.bodyClearance + 0.5f;
    }

    void BuildLegRuntime()
    {
        legs = new LegRuntime[phenotype.legs.Length];
        supportPoints = new Vector2[Mathf.Max(4, legs.Length)];
        for (int i = 0; i < legs.Length; i++)
        {
            CreaturePhenotypeLeg definition = phenotype.legs[i];
            legs[i] = new LegRuntime
            {
                definition = definition,
                upper = rig.nodeBones[definition.upperNode],
                lower = rig.nodeBones[definition.lowerNode],
                distal = rig.nodeBones[definition.distalNode],
                foot = rig.nodeBones[definition.footNode],
                groundNormal = Vector3.up,
                plantedNormal = Vector3.up,
                swingTargetNormal = Vector3.up,
                stance = true,
                previousStance = true
            };
        }
    }

    bool EvaluateSupportPolygon(Vector3 right, Vector3 forward)
    {
        int count = 0;
        Vector2 center = Vector2.zero;
        for (int i = 0; i < legs.Length; i++)
        {
            if (!legs[i].stance || !legs[i].initialized) continue;
            Vector3 offset = legs[i].plantedPosition - body.position;
            Vector2 point = new Vector2(Vector3.Dot(offset, right), Vector3.Dot(offset, forward));
            supportPoints[count++] = point;
            center += point;
        }
        if (count < 2) return false;
        if (count == 2)
        {
            Vector3 twoFootCenterOfMass = transform.TransformPoint(phenotype.centerOfMass);
            Vector3 comOffsetWorld = twoFootCenterOfMass - body.position;
            Vector2 comPoint = new Vector2(
                Vector3.Dot(comOffsetWorld, right), Vector3.Dot(comOffsetWorld, forward));
            Vector2 segment = supportPoints[1] - supportPoints[0];
            float segmentT = Mathf.Clamp01(Vector2.Dot(comPoint - supportPoints[0], segment)
                / Mathf.Max(0.0001f, segment.sqrMagnitude));
            float distance = Vector2.Distance(comPoint, supportPoints[0] + segment * segmentT);
            return distance <= Mathf.Max(genome.footScale * 0.9f, genome.bodyWidth * 0.32f);
        }
        center /= count;

        for (int i = 1; i < count; i++)
        {
            Vector2 value = supportPoints[i];
            float angle = Mathf.Atan2(value.y - center.y, value.x - center.x);
            int insertion = i - 1;
            while (insertion >= 0)
            {
                Vector2 previous = supportPoints[insertion];
                float previousAngle = Mathf.Atan2(previous.y - center.y, previous.x - center.x);
                if (previousAngle <= angle) break;
                supportPoints[insertion + 1] = previous;
                insertion--;
            }
            supportPoints[insertion + 1] = value;
        }

        Vector3 centerOfMassWorld = transform.TransformPoint(phenotype.centerOfMass);
        Vector3 comOffset = centerOfMassWorld - body.position;
        Vector2 com = new Vector2(Vector3.Dot(comOffset, right), Vector3.Dot(comOffset, forward));
        float sign = 0f;
        float supportMargin = Mathf.Max(genome.footScale * 0.35f, genome.bodyWidth * 0.08f);
        for (int i = 0; i < count; i++)
        {
            Vector2 a = supportPoints[i];
            Vector2 b = supportPoints[(i + 1) % count];
            float cross = (b.x - a.x) * (com.y - a.y) - (b.y - a.y) * (com.x - a.x);
            if (Mathf.Abs(cross) < 0.0001f) continue;
            float currentSign = Mathf.Sign(cross);
            if (sign == 0f) sign = currentSign;
            else if (currentSign != sign)
            {
                float edgeLength = Vector2.Distance(a, b);
                if (Mathf.Abs(cross) / Mathf.Max(0.0001f, edgeLength) > supportMargin)
                    return false;
            }
        }
        return true;
    }

    void UpdatePlanningLod()
    {
        if (lodCamera == null) lodCamera = Camera.main;
        if (lodCamera == null)
        {
            planningInterval = 0f;
            return;
        }
        float distance = Vector3.Distance(lodCamera.transform.position, transform.position);
        planningInterval = distance < 22f ? 0f : distance < 60f ? 0.04f : 0.1f;
    }

    float GetWalkOffset(CreaturePhenotypeLeg leg)
    {
        if (phenotype.locomotionArchetype == CreatureLocomotionArchetype.HexapodTripod)
        {
            float sideOffset = leg.IsLeft ? 0f : 0.5f;
            return Mathf.Repeat((1f - leg.longitudinalPosition) * 0.42f + sideOffset, 1f);
        }
        if (leg.IsFront && leg.IsLeft) return 0f;
        if (!leg.IsFront && !leg.IsLeft) return 0.25f;
        if (leg.IsFront) return 0.5f;
        return 0.75f;
    }

    float CalculateDutyFactor(float speed01)
    {
        if (phenotype.locomotionArchetype == CreatureLocomotionArchetype.HexapodTripod)
            return Mathf.Lerp(0.72f, 0.5f, speed01);
        if (phenotype.locomotionArchetype == CreatureLocomotionArchetype.GraviportalElephant)
            return Mathf.Lerp(phenotype.slowStanceDutyFactor, phenotype.fastStanceDutyFactor, speed01);

        float speed = DesiredVelocity.magnitude;
        if (speed <= phenotype.walkSpeed * 1.05f)
            return 0.78f;
        if (speed <= phenotype.trotSpeed * 1.05f)
        {
            float blend = Mathf.InverseLerp(phenotype.walkSpeed, phenotype.trotSpeed, speed);
            return Mathf.Lerp(0.72f, 0.56f, blend);
        }
        float runBlend = Mathf.InverseLerp(phenotype.trotSpeed, phenotype.runSpeed, speed);
        return Mathf.Lerp(0.54f, phenotype.fastStanceDutyFactor, runBlend);
    }

    float GetTrotOffset(CreaturePhenotypeLeg leg)
    {
        if (phenotype.locomotionArchetype == CreatureLocomotionArchetype.HexapodTripod)
            return leg.gaitGroup * 0.5f;
        return leg.IsFront == leg.IsLeft ? 0f : 0.5f;
    }

    float GetGallopOffset(CreaturePhenotypeLeg leg)
    {
        if (phenotype.locomotionArchetype == CreatureLocomotionArchetype.HexapodTripod)
            return leg.gaitGroup * 0.5f;
        if (!leg.IsFront && leg.IsLeft) return 0f;
        if (!leg.IsFront) return 0.1f;
        if (leg.IsLeft) return 0.52f;
        return 0.62f;
    }
}

[DisallowMultipleComponent]
public sealed class CreatureProceduralTelemetryHud : MonoBehaviour
{
    CreatureProceduralController controller;
    CreatureGenome genome;
    StringBuilder builder;
    string telemetry = string.Empty;
    float generationMilliseconds;
    float nextRefresh;

    public void Configure(
        CreatureProceduralController sourceController,
        CreatureGenome sourceGenome,
        float buildMilliseconds)
    {
        controller = sourceController;
        genome = sourceGenome;
        generationMilliseconds = buildMilliseconds;
        RefreshText();
    }

    void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.2f;
        RefreshText();
    }

    void RefreshText()
    {
        if (controller == null || genome == null) return;
        CreaturePhenotype currentPhenotype = controller.Phenotype;
        if (currentPhenotype == null || currentPhenotype.legs == null)
        {
            telemetry = $"BIO V{genome.generatorVersion}  Seed {genome.seed}  Initializing...";
            return;
        }

        if (builder == null) builder = new StringBuilder(256);
        builder.Length = 0;
        builder.Append("BIO V").Append(genome.generatorVersion).Append("  Seed ").Append(genome.seed)
            .Append("  ").Append(genome.locomotionArchetype)
            .Append("  Shape ").Append(currentPhenotype.shapeHash).AppendLine();
        builder.Append("State ").Append(controller.CurrentState)
            .Append("  Speed ").Append(controller.CurrentSpeed.ToString("F2"))
            .Append('/').Append(currentPhenotype.runSpeed.ToString("F2")).AppendLine();
        builder.Append("Feet ").Append(controller.ContactFootCount).Append('/').Append(currentPhenotype.legs.Length)
            .Append("  Stable ").Append(controller.IsStable ? "YES" : "NO")
            .Append("  Build ").Append(generationMilliseconds.ToString("F0")).Append(" ms");
        telemetry = builder.ToString();
    }

    void OnGUI()
    {
        if (controller == null) return;
        GUI.Box(new Rect(16f, 16f, 520f, 78f), GUIContent.none);
        GUI.Label(new Rect(28f, 24f, 500f, 64f), telemetry);
    }
}
