using System;
using System.Collections.Generic;
using UnityEngine;

public enum NmsProceduralPhysicsMode
{
    GroundAdhesion,
    Dynamic
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public sealed class NmsProceduralMotionController : MonoBehaviour
{
    sealed class LegRuntime
    {
        public string id;
        public Transform upper;
        public Transform lower;
        public Transform distal;
        public Transform foot;
        public Quaternion upperRest;
        public Quaternion lowerRest;
        public Quaternion distalRest;
        public Quaternion footRest;
        public NmsProceduralLegProfile profile;
        public NmsProceduralGaitLegTrack gaitTrack;
        public float upperLength;
        public float lowerLength;
        public float distalLength;
        public float totalLength;
        public float footPivotClearance;
        public Vector3 restFirstBendRootLocal;
        public Vector3 restSecondBendRootLocal;
        public Vector3 templateFirstPoleRootLocal;
        public Vector3 templateSecondPoleRootLocal;
        public Vector3 currentFirstPoleWorld;
        public Vector3 currentSecondPoleWorld;
        public bool poleInitialized;
        public float poleTemplateWeight;
        public float firstPoleAngularDelta;
        public float secondPoleAngularDelta;
        public string poleFallbackReason = "None";
        public Vector3 restGroundOffsetLocal;
        public Vector3 plantedPosition;
        public Vector3 plantedNormal;
        public Vector3 swingStart;
        public Vector3 swingTarget;
        public Vector3 swingTargetNormal;
        public bool initialized;
        public bool stance;
        public bool previousStance;
        public bool emergencySwing;
        public float emergencySwingProgress;
        public float trajectoryResidual;
        public float strideScale = 1f;
        public string trajectoryMode = "Stance";
        public bool ikReachable = true;
    }

    sealed class BoneTrackRuntime
    {
        public Transform bone;
        public Vector3 restPosition;
        public Quaternion restRotation;
        public NmsProceduralGaitBoneTrack track;
    }

    const float CommandTimeout = 0.3f;
    static readonly string[] LegStateLabels =
    {
        "SSSS", "WSSS", "SWSS", "WWSS",
        "SSWS", "WSWS", "SWWS", "WWWS",
        "SSSW", "WSSW", "SWSW", "WWSW",
        "SSWW", "WSWW", "SWWW", "WWWW"
    };
    readonly RaycastHit[] groundHits = new RaycastHit[16];
    readonly List<LegRuntime> legs = new List<LegRuntime>(4);
    readonly List<BoneTrackRuntime> bodyTracks = new List<BoneTrackRuntime>(8);
    readonly float[] sampledStance = new float[4];
    readonly float[] sampledSwingProgress = new float[4];
    readonly Vector3[] sampledFootOffsets = new Vector3[4];
    readonly Vector3[] sampledFirstPoles = new Vector3[4];
    readonly Vector3[] sampledSecondPoles = new Vector3[4];

    SphericalGravitySource gravitySource;
    NmsCreatureFamilyDefinition family;
    NmsCreatureSpeciesDefinition species;
    NmsProceduralMotionProfile profile;
    LayerMask groundLayers;
    Rigidbody body;
    CapsuleCollider capsule;
    PhysicMaterial bodyMaterial;
    Transform bodyBone;
    Transform neckBone;
    Transform headBone;
    Transform tailBone;
    Quaternion bodyRest;
    Vector3 bodyRestPosition;
    Quaternion neckRest;
    Quaternion headRest;
    Quaternion tailRest;
    CreatureMotionCommand command;
    Vector3 surfaceForward;
    Vector3 localForwardAxis;
    Vector3 groundNormal = Vector3.up;
    float commandAge;
    float stateTime;
    float gaitPhase;
    float gaitBlend;
    float rigScale = 1f;
    float restBodyHeight;
    float currentGaitLift;
    float timeWithoutGround;
    float currentBodyHeight;
    Vector3 lastGroundPoint;
    bool hasGroundReference;
    bool rawGroundContact;
    bool hasCommand;
    bool pendingJump;
    bool pendingAttack;
    bool jumpImpulseSent;
    bool attackImpulseSent;
    bool attackImpactSent;

    public CreatureProceduralActionState CurrentState { get; private set; }
    public Vector3 DesiredVelocity { get; private set; }
    public Vector3 SurfaceForward => surfaceForward;
    public Vector3 GroundNormal => groundNormal;
    public float CurrentSpeed { get; private set; }
    public int ContactFootCount { get; private set; }
    public bool IsGrounded { get; private set; }
    public bool IsStable { get; private set; }
    public bool AnimatorDisabled { get; private set; }
    public float MaxStanceFootError { get; private set; }
    public float MaxSegmentLengthError { get; private set; }
    public int ReachRecoveryLegCount { get; private set; }
    public NmsProceduralPhysicsMode PhysicsMode { get; private set; }
    public float BodyHeightError { get; private set; }
    public float NormalVelocity { get; private set; }
    public float GaitPhase => gaitPhase;
    public string LastAirborneReason { get; private set; } = "None";
    public string LegStateSummary { get; private set; } = "----";
    public float PoleTemplateWeight { get; private set; }
    public float MaxKneePoleDelta { get; private set; }
    public float MaxHockPoleDelta { get; private set; }
    public float MaxTemplateTrajectoryResidual { get; private set; }
    public float TemplateStrideScale { get; private set; } = 1f;
    public bool AllLegsIkReachable { get; private set; } = true;
    public string TrajectoryMode { get; private set; } = "Stance";
    public string PoleFallbackReason { get; private set; } = "None";
    public NmsCreatureSpeciesDefinition Species => species;
    public float WalkSpeed => family != null ? family.WalkSpeed : 0f;
    public float RunSpeed => family != null && profile != null
        ? family.WalkSpeed * profile.RunSpeedMultiplier : 0f;
    public event Action<CreatureAttackImpact> OnAttackImpact;

    public bool Configure(
        SphericalGravitySource source,
        NmsCreatureFamilyDefinition sourceFamily,
        NmsCreatureSpeciesDefinition sourceSpecies,
        NmsProceduralMotionProfile sourceProfile,
        LayerMask layers,
        Animator animator,
        out string error)
    {
        error = null;
        gravitySource = source;
        family = sourceFamily;
        species = sourceSpecies;
        profile = sourceProfile;
        groundLayers = layers;
        if (gravitySource == null || family == null || profile == null)
        {
            error = "Procedural motion requires gravity, a family and a motion profile.";
            return false;
        }
        if (!string.Equals(family.FamilyId, profile.FamilyId, StringComparison.Ordinal))
        {
            error = $"Motion profile '{profile.FamilyId}' cannot drive family '{family.FamilyId}'.";
            return false;
        }
        if (family.LoadBearingChains.Count != 4)
        {
            error = $"Family '{family.FamilyId}' is not a four-leg procedural target.";
            return false;
        }
        NmsProceduralGaitTemplate gaitTemplate = profile.WalkGaitTemplate;
        if (gaitTemplate == null
            || !gaitTemplate.IsValidFor(family.FamilyId, 4, out error))
        {
            error = "A valid offline-extracted WALK gait template is required. " + error;
            return false;
        }

        body = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();

        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;
            animator.enabled = false;
            AnimatorDisabled = true;
        }
        else
        {
            AnimatorDisabled = true;
        }

        var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        Transform[] hierarchy = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < hierarchy.Length; i++)
            if (!bones.ContainsKey(hierarchy[i].name))
                bones.Add(hierarchy[i].name, hierarchy[i]);

        if (!ValidateProceduralLegRig(bones, out error))
            return false;

        bodyBone = FindBone(bones, profile.BodyBoneName);
        neckBone = FindBone(bones, profile.NeckBoneName);
        headBone = FindBone(bones, profile.HeadBoneName);
        tailBone = FindBone(bones, profile.TailBoneName);
        if (bodyBone == null || neckBone == null || headBone == null || tailBone == null)
        {
            error = "The procedural body, neck, head, or tail bone is missing.";
            return false;
        }
        if (bodyBone != null)
        {
            bodyRest = bodyBone.localRotation;
            bodyRestPosition = bodyBone.localPosition;
        }
        if (neckBone != null) neckRest = neckBone.localRotation;
        if (headBone != null) headRest = headBone.localRotation;
        if (tailBone != null) tailRest = tailBone.localRotation;
        bodyTracks.Clear();
        NmsProceduralGaitBoneTrack[] gaitBoneTracks = gaitTemplate.BoneTracks;
        for (int i = 0; i < gaitBoneTracks.Length; i++)
        {
            NmsProceduralGaitBoneTrack track = gaitBoneTracks[i];
            Transform bone = FindBone(bones, track.BoneName);
            if (bone == null)
            {
                error = $"Gait template bone '{track.BoneName}' is missing from the runtime rig.";
                return false;
            }
            bodyTracks.Add(new BoneTrackRuntime
            {
                bone = bone,
                restPosition = bone.localPosition,
                restRotation = bone.localRotation,
                track = track
            });
        }
        localForwardAxis = family.LocalForwardAxis.sqrMagnitude > 0.000001f
            ? family.LocalForwardAxis.normalized
            : Vector3.forward;
        if (headBone != null && tailBone != null)
        {
            Vector3 up = gravitySource.GetUp(transform.position);
            Vector3 anatomicalForward = Vector3.ProjectOnPlane(
                headBone.position - tailBone.position, up);
            if (anatomicalForward.sqrMagnitude > 0.0001f)
                localForwardAxis = transform.InverseTransformDirection(
                    anatomicalForward.normalized).normalized;
        }

        legs.Clear();
        float totalRigLength = 0f;
        for (int i = 0; i < family.LoadBearingChains.Count; i++)
        {
            NmsCreatureLegChain chain = family.LoadBearingChains[i];
            if (chain == null || chain.bones == null || chain.bones.Length < 4
                || !profile.TryGetLeg(chain.id, out NmsProceduralLegProfile legProfile)
                || !gaitTemplate.TryGetLeg(
                    chain.id, out NmsProceduralGaitLegTrack gaitLegTrack))
            {
                error = $"Missing procedural metadata for leg chain '{chain?.id ?? "<null>"}'.";
                return false;
            }
            Transform upper = FindBone(bones, chain.bones[0]);
            Transform lower = FindBone(bones, chain.bones[1]);
            Transform distal = FindBone(bones, chain.bones[2]);
            Transform foot = FindBone(bones, chain.bones[3]);
            if (upper == null || lower == null || distal == null || foot == null)
            {
                error = $"Leg chain '{chain.id}' has a missing bone.";
                return false;
            }
            if (lower.parent != upper || distal.parent != lower || foot.parent != distal)
            {
                error = $"Leg chain '{chain.id}' is not a contiguous hierarchy.";
                return false;
            }
            float upperLength = Vector3.Distance(upper.position, lower.position);
            float lowerLength = Vector3.Distance(lower.position, distal.position);
            float distalLength = Vector3.Distance(distal.position, foot.position);
            if (!FinitePositive(upperLength) || !FinitePositive(lowerLength)
                || !FinitePositive(distalLength))
            {
                error = $"Leg chain '{chain.id}' contains an invalid segment length.";
                return false;
            }
            var leg = new LegRuntime
            {
                id = chain.id,
                upper = upper,
                lower = lower,
                distal = distal,
                foot = foot,
                upperRest = upper.localRotation,
                lowerRest = lower.localRotation,
                distalRest = distal.localRotation,
                footRest = foot.localRotation,
                profile = legProfile,
                gaitTrack = gaitLegTrack,
                upperLength = upperLength,
                lowerLength = lowerLength,
                distalLength = distalLength,
                totalLength = upperLength + lowerLength + distalLength,
                stance = true,
                previousStance = true
            };
            Vector3 creatureUp = gravitySource.GetUp(transform.position);
            leg.footPivotClearance = Mathf.Max(
                rigScale * 0.01f,
                Vector3.Dot(foot.position - transform.position, creatureUp));
            Vector3 profileBend = transform.TransformDirection(legProfile.bendHintLocal);
            Vector3 firstBend = RestBendDirection(
                lower.position - upper.position,
                distal.position - upper.position,
                profileBend);
            Vector3 secondBend = RestBendDirection(
                distal.position - lower.position,
                foot.position - lower.position,
                firstBend);
            leg.restFirstBendRootLocal = transform.InverseTransformDirection(firstBend);
            leg.restSecondBendRootLocal = transform.InverseTransformDirection(secondBend);
            Vector3 rootUpper = transform.InverseTransformPoint(upper.position);
            Vector3 rootFoot = transform.InverseTransformPoint(foot.position);
            leg.restGroundOffsetLocal = rootFoot - rootUpper;
            totalRigLength += leg.totalLength;
            legs.Add(leg);
        }
        rigScale = Mathf.Max(0.05f, totalRigLength / legs.Count);

        Vector3 calibrationUp = gravitySource.GetUp(transform.position);
        restBodyHeight = 0f;
        for (int i = 0; i < legs.Count; i++)
        {
            LegRuntime leg = legs[i];
            leg.footPivotClearance = Mathf.Max(
                rigScale * 0.008f,
                Vector3.Dot(leg.foot.position - transform.position, calibrationUp));
            Vector3 sole = leg.foot.position - calibrationUp * leg.footPivotClearance;
            restBodyHeight += Vector3.Dot(bodyBone.position - sole, calibrationUp);
        }
        restBodyHeight = Mathf.Max(rigScale * 0.2f, restBodyHeight / legs.Count);

        body.mass = profile.Mass;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        ConfigureCollider();
        surfaceForward = Vector3.ProjectOnPlane(
            transform.TransformDirection(localForwardAxis),
            gravitySource.GetUp(transform.position)).normalized;
        if (surfaceForward.sqrMagnitude < 0.001f)
            surfaceForward = transform.forward;
        CurrentState = CreatureProceduralActionState.Idle;
        PhysicsMode = NmsProceduralPhysicsMode.GroundAdhesion;
        ResetFootContacts();
        return true;
    }

    bool ValidateProceduralLegRig(
        Dictionary<string, Transform> bones, out string error)
    {
        error = null;
        for (int i = 0; i < family.LoadBearingChains.Count; i++)
        {
            NmsCreatureLegChain chain = family.LoadBearingChains[i];
            if (chain == null || chain.bones == null || chain.bones.Length < 4)
            {
                error = "Procedural motion requires four bones per leg.";
                return false;
            }
            Transform upper = FindBone(bones, chain.bones[0]);
            Transform lower = FindBone(bones, chain.bones[1]);
            Transform distal = FindBone(bones, chain.bones[2]);
            Transform foot = FindBone(bones, chain.bones[3]);
            if (upper == null || lower == null || distal == null || foot == null)
            {
                error = $"Leg chain '{chain.id}' is incomplete.";
                return false;
            }
            if (lower.parent != upper || distal.parent != lower || foot.parent != distal)
            {
                error = $"Leg chain '{chain.id}' is not a contiguous hierarchy.";
                return false;
            }
            Vector3 segmentLengths = new Vector3(
                Vector3.Distance(upper.position, lower.position),
                Vector3.Distance(lower.position, distal.position),
                Vector3.Distance(distal.position, foot.position));
            if (!FinitePositive(segmentLengths.x)
                || !FinitePositive(segmentLengths.y)
                || !FinitePositive(segmentLengths.z))
            {
                error = $"Leg chain '{chain.id}' has an invalid bind pose.";
                return false;
            }
        }
        return true;
    }

    public void SetCommand(CreatureMotionCommand value)
    {
        command = value;
        hasCommand = true;
        commandAge = 0f;
        pendingJump |= value.jumpRequested;
        pendingAttack |= value.attackRequested;
    }

    public void ResetFootContacts()
    {
        for (int i = 0; i < legs.Count; i++)
        {
            legs[i].initialized = false;
            legs[i].stance = true;
            legs[i].previousStance = true;
            legs[i].emergencySwing = false;
            legs[i].emergencySwingProgress = 0f;
            legs[i].poleInitialized = false;
            legs[i].poleFallbackReason = "None";
        }
        ContactFootCount = 0;
        IsStable = false;
    }

    void FixedUpdate()
    {
        if (body == null || gravitySource == null || profile == null)
            return;
        float dt = Mathf.Max(0.001f, Time.fixedDeltaTime);
        commandAge += dt;
        if (commandAge > CommandTimeout)
        {
            hasCommand = false;
            command = default;
        }

        Vector3 up = gravitySource.GetUp(body.worldCenterOfMass);
        rawGroundContact = ProbeBodyGround(
            up, out RaycastHit bodyGround, out currentBodyHeight);
        if (rawGroundContact)
        {
            timeWithoutGround = 0f;
            hasGroundReference = true;
            lastGroundPoint = bodyGround.point;
            Vector3 supportNormal = CalculateSupportNormal(bodyGround.normal);
            groundNormal = Vector3.Slerp(
                groundNormal, supportNormal,
                1f - Mathf.Exp(-14f * dt)).normalized;
        }
        else
        {
            timeWithoutGround += dt;
            groundNormal = Vector3.Slerp(
                groundNormal, up, 1f - Mathf.Exp(-5f * dt)).normalized;
        }
        IsGrounded = rawGroundContact
            || (hasGroundReference && timeWithoutGround <= profile.GroundGraceTime);

        Vector3 movementNormal = IsGrounded ? groundNormal : up;
        Vector3 requested = hasCommand
            ? Vector3.ProjectOnPlane(command.desiredVelocityWorld, movementNormal)
            : Vector3.zero;
        float runSpeed = family.WalkSpeed * profile.RunSpeedMultiplier;
        DesiredVelocity = Vector3.ClampMagnitude(requested, runSpeed);
        Vector3 velocity = Vector3.ProjectOnPlane(body.velocity, movementNormal);
        CurrentSpeed = velocity.magnitude;
        NormalVelocity = Vector3.Dot(body.velocity, groundNormal);
        float desiredBodyHeight = restBodyHeight + Mathf.Clamp(
            currentGaitLift, -rigScale * 0.04f, rigScale * 0.04f);
        BodyHeightError = desiredBodyHeight - currentBodyHeight;

        UpdateState(dt, up);
        UpdatePhysicsMode();
        ApplyGravityAndGroundAdhesion(up, desiredBodyHeight);
        UpdateHeading(dt, up);
        ApplyMovement(velocity, up);
        UpdateGaitPhase(dt);
        UpdateAttackImpact(up);
    }

    void LateUpdate()
    {
        if (body == null || gravitySource == null || legs.Count == 0)
            return;
        ResetControlledPose();
        Vector3 up = gravitySource.GetUp(transform.position);
        Vector3 forward = Vector3.ProjectOnPlane(surfaceForward, up).normalized;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        ApplyFullBodyMotion(up);
        UpdateLegPoses(up, forward);
    }

    void UpdateState(float dt, Vector3 up)
    {
        stateTime += dt;
        if (CurrentState == CreatureProceduralActionState.Airborne)
        {
            if (rawGroundContact && NormalVelocity <= rigScale * 0.25f
                && stateTime > 0.08f)
                EnterState(CreatureProceduralActionState.Landing);
            return;
        }
        bool separatedFromGround = timeWithoutGround > profile.GroundGraceTime
            && (!hasGroundReference
                || Mathf.Abs(BodyHeightError) > rigScale * 0.12f
                || Mathf.Abs(NormalVelocity) > rigScale * 0.25f);
        if (separatedFromGround
            && CurrentState != CreatureProceduralActionState.JumpPrepare)
        {
            LastAirborneReason = rawGroundContact
                ? "HeightOrVelocityThreshold" : "GroundProbeTimeout";
            EnterState(CreatureProceduralActionState.Airborne);
            return;
        }
        if (CurrentState == CreatureProceduralActionState.JumpPrepare)
        {
            if (!jumpImpulseSent && stateTime >= 0.24f)
            {
                float impulse = Mathf.Sqrt(2f * gravitySource.SurfaceGravity
                    * profile.JumpHeight * rigScale);
                body.AddForce(up * impulse, ForceMode.VelocityChange);
                jumpImpulseSent = true;
                LastAirborneReason = "JumpRequested";
                EnterState(CreatureProceduralActionState.Airborne);
            }
            return;
        }
        if (CurrentState == CreatureProceduralActionState.Landing)
        {
            if (stateTime >= 0.34f)
                EnterState(DesiredVelocity.sqrMagnitude > 0.01f
                    ? CreatureProceduralActionState.Start
                    : CreatureProceduralActionState.Idle);
            return;
        }
        if (CurrentState == CreatureProceduralActionState.AttackWindup)
        {
            if (stateTime >= 0.22f)
                EnterState(CreatureProceduralActionState.AttackStrike);
            return;
        }
        if (CurrentState == CreatureProceduralActionState.AttackStrike)
        {
            if (!attackImpulseSent)
            {
                body.AddForce(surfaceForward * profile.AttackImpulse, ForceMode.VelocityChange);
                attackImpulseSent = true;
            }
            if (stateTime >= 0.16f)
                EnterState(CreatureProceduralActionState.AttackRecover);
            return;
        }
        if (CurrentState == CreatureProceduralActionState.AttackRecover)
        {
            if (stateTime >= 0.3f)
                EnterState(DesiredVelocity.sqrMagnitude > 0.01f
                    ? CreatureProceduralActionState.Start
                    : CreatureProceduralActionState.Idle);
            return;
        }

        if (pendingJump && IsGrounded && IsStable)
        {
            pendingJump = false;
            EnterState(CreatureProceduralActionState.JumpPrepare);
            return;
        }
        if (pendingAttack && IsGrounded)
        {
            pendingAttack = false;
            EnterState(CreatureProceduralActionState.AttackWindup);
            return;
        }

        float desiredSpeed = DesiredVelocity.magnitude;
        switch (CurrentState)
        {
            case CreatureProceduralActionState.Idle:
                if (desiredSpeed > 0.05f) EnterState(CreatureProceduralActionState.Start);
                break;
            case CreatureProceduralActionState.Start:
                if (desiredSpeed <= 0.05f) EnterState(CreatureProceduralActionState.Stop);
                else if (stateTime > 0.2f)
                    EnterState(desiredSpeed > family.WalkSpeed * 1.3f
                        ? CreatureProceduralActionState.Run
                        : CreatureProceduralActionState.Walk);
                break;
            case CreatureProceduralActionState.Walk:
                if (desiredSpeed <= 0.05f) EnterState(CreatureProceduralActionState.Stop);
                else if (desiredSpeed > family.WalkSpeed * 1.3f)
                    EnterState(CreatureProceduralActionState.Run);
                break;
            case CreatureProceduralActionState.Run:
                if (desiredSpeed <= 0.05f) EnterState(CreatureProceduralActionState.Stop);
                else if (desiredSpeed < family.WalkSpeed * 1.15f)
                    EnterState(CreatureProceduralActionState.Walk);
                break;
            case CreatureProceduralActionState.Stop:
                if (desiredSpeed > 0.05f) EnterState(CreatureProceduralActionState.Start);
                else if (CurrentSpeed < 0.08f) EnterState(CreatureProceduralActionState.Idle);
                break;
        }
    }

    void EnterState(CreatureProceduralActionState next)
    {
        if (CurrentState == next) return;
        CurrentState = next;
        stateTime = 0f;
        if (next == CreatureProceduralActionState.JumpPrepare) jumpImpulseSent = false;
        if (next == CreatureProceduralActionState.AttackWindup)
        {
            attackImpulseSent = false;
            attackImpactSent = false;
        }
        if (next == CreatureProceduralActionState.Landing) ResetFootContacts();
    }

    void UpdatePhysicsMode()
    {
        switch (CurrentState)
        {
            case CreatureProceduralActionState.Airborne:
            case CreatureProceduralActionState.AttackStrike:
                PhysicsMode = NmsProceduralPhysicsMode.Dynamic;
                break;
            default:
                PhysicsMode = NmsProceduralPhysicsMode.GroundAdhesion;
                break;
        }
    }

    void ApplyGravityAndGroundAdhesion(Vector3 radialUp, float desiredBodyHeight)
    {
        Vector3 gravity = gravitySource.GetGravity(body.worldCenterOfMass);
        body.AddForce(gravity, ForceMode.Acceleration);
        if (PhysicsMode != NmsProceduralPhysicsMode.GroundAdhesion
            || !IsGrounded || !hasGroundReference)
            return;

        Vector3 normal = groundNormal.sqrMagnitude > 0.001f
            ? groundNormal.normalized : radialUp;
        float height = rawGroundContact
            ? currentBodyHeight
            : Vector3.Dot(bodyBone.position - lastGroundPoint, normal);
        BodyHeightError = desiredBodyHeight - height;
        NormalVelocity = Vector3.Dot(body.velocity, normal);
        float gravityAlongNormal = Vector3.Dot(gravity, normal);
        float acceleration = profile.GroundHeightKp * BodyHeightError
            - profile.GroundHeightKd * NormalVelocity
            - gravityAlongNormal;
        float maximum = gravitySource.SurfaceGravity
            * profile.MaximumAdhesionGravity;
        body.AddForce(normal * Mathf.Clamp(acceleration, -maximum, maximum),
            ForceMode.Acceleration);
    }

    Vector3 CalculateSupportNormal(Vector3 bodyGroundNormal)
    {
        Vector3 sum = bodyGroundNormal;
        int count = 1;
        for (int i = 0; i < legs.Count; i++)
        {
            LegRuntime leg = legs[i];
            if (!leg.initialized || !leg.stance
                || leg.plantedNormal.sqrMagnitude < 0.5f)
                continue;
            sum += leg.plantedNormal;
            count++;
        }
        return count > 0 && sum.sqrMagnitude > 0.001f
            ? (sum / count).normalized : bodyGroundNormal;
    }

    bool HasSwingLeg()
    {
        for (int i = 0; i < legs.Count; i++)
            if (legs[i].initialized && !legs[i].stance)
                return true;
        return false;
    }

    void ApplyMovement(Vector3 tangentialVelocity, Vector3 up)
    {
        if (PhysicsMode != NmsProceduralPhysicsMode.GroundAdhesion
            || !IsGrounded || CurrentState == CreatureProceduralActionState.JumpPrepare
            || CurrentState == CreatureProceduralActionState.AttackWindup)
            return;
        float support = Mathf.Max(0.35f, Mathf.Clamp01(ContactFootCount / 3f));
        float accelerationLimit = Mathf.Lerp(5f, 18f, support);
        Vector3 acceleration = (DesiredVelocity - tangentialVelocity) * 8f;
        acceleration = Vector3.ProjectOnPlane(acceleration, groundNormal);
        body.AddForce(Vector3.ClampMagnitude(acceleration, accelerationLimit),
            ForceMode.Acceleration);
    }

    void UpdateHeading(float dt, Vector3 up)
    {
        Vector3 requestedFacing = hasCommand
            ? Vector3.ProjectOnPlane(command.desiredFacingWorld, up)
            : Vector3.zero;
        if (requestedFacing.sqrMagnitude < 0.001f && DesiredVelocity.sqrMagnitude > 0.001f)
            requestedFacing = DesiredVelocity;
        if (requestedFacing.sqrMagnitude > 0.001f)
            surfaceForward = Vector3.Slerp(surfaceForward, requestedFacing.normalized,
                1f - Mathf.Exp(-8f * dt)).normalized;
        Vector3 alignmentUp = PhysicsMode == NmsProceduralPhysicsMode.GroundAdhesion
            && IsGrounded ? groundNormal : up;
        surfaceForward = Vector3.ProjectOnPlane(surfaceForward, alignmentUp).normalized;
        Quaternion target = RotationForLocalForward(
            localForwardAxis, surfaceForward, alignmentUp);
        body.MoveRotation(Quaternion.RotateTowards(body.rotation, target, 75f * dt));
    }

    void UpdateGaitPhase(float dt)
    {
        bool requestedMovement = DesiredVelocity.sqrMagnitude >= 0.003f;
        bool finishingStep = CurrentState == CreatureProceduralActionState.Stop
            && HasSwingLeg();
        float targetBlend = requestedMovement || finishingStep ? 1f : 0f;
        gaitBlend = Mathf.MoveTowards(gaitBlend, targetBlend, dt * 4f);
        if (gaitBlend <= 0.001f)
            return;

        float phaseSpeed = CurrentSpeed;
        if (CurrentState == CreatureProceduralActionState.Start)
            phaseSpeed = Mathf.Max(phaseSpeed, DesiredVelocity.magnitude * 0.28f);
        if (finishingStep)
            phaseSpeed = Mathf.Max(phaseSpeed, family.WalkSpeed * 0.18f);
        float cycleDistance = Mathf.Max(
            rigScale * 0.1f,
            profile.WalkGaitTemplate.NormalizedDistancePerCycle * rigScale);
        gaitPhase = Mathf.Repeat(gaitPhase + phaseSpeed * dt / cycleDistance, 1f);
    }

    void UpdateLegPoses(Vector3 up, Vector3 forward)
    {
        bool moving = gaitBlend > 0.05f
            && (DesiredVelocity.sqrMagnitude > 0.003f
                || CurrentSpeed > 0.04f || HasSwingLeg());
        int contacts = 0;
        int reachRecoveries = 0;
        int recoverySlotsUsed = 0;
        float maxStanceError = 0f;
        float maxSegmentError = 0f;
        float poleWeightSum = 0f;
        PoleTemplateWeight = 0f;
        MaxKneePoleDelta = 0f;
        MaxHockPoleDelta = 0f;
        MaxTemplateTrajectoryResidual = 0f;
        TemplateStrideScale = 1f;
        AllLegsIkReachable = true;
        TrajectoryMode = "Stance";
        PoleFallbackReason = "None";
        for (int i = 0; i < legs.Count; i++)
            if (legs[i].emergencySwing)
                recoverySlotsUsed++;
        for (int i = 0; i < legs.Count; i++)
        {
            legs[i].gaitTrack.Sample(
                gaitPhase, out sampledFootOffsets[i], out sampledStance[i],
                out sampledSwingProgress[i], out sampledFirstPoles[i],
                out sampledSecondPoles[i]);
        }
        EnsureMinimumTemplateSupport(moving);
        int stateMask = 0;
        for (int i = 0; i < legs.Count; i++)
        {
            LegRuntime leg = legs[i];
            leg.templateFirstPoleRootLocal = sampledFirstPoles[i];
            leg.templateSecondPoleRootLocal = sampledSecondPoles[i];
            leg.trajectoryMode = "Stance";
            leg.trajectoryResidual = 0f;
            leg.strideScale = 1f;
            if (!leg.initialized)
            {
                GroundFoot(leg.foot.position, up, leg.totalLength, leg.footPivotClearance,
                    out leg.plantedPosition,
                    out leg.plantedNormal);
                leg.swingStart = leg.plantedPosition;
                leg.swingTarget = leg.plantedPosition;
                leg.swingTargetNormal = leg.plantedNormal;
                leg.initialized = true;
            }

            Vector3 target;
            Vector3 targetNormal = leg.plantedNormal;
            if (CurrentState == CreatureProceduralActionState.Airborne)
            {
                leg.stance = false;
                leg.trajectoryMode = "Emergency";
                Vector3 side = transform.TransformVector(
                    Vector3.ProjectOnPlane(leg.restGroundOffsetLocal, Vector3.up));
                target = leg.upper.position + side * 0.7f - up * leg.totalLength * 0.52f;
            }
            else
            {
                leg.stance = !moving || sampledStance[i] >= 0.5f
                    || CurrentState == CreatureProceduralActionState.JumpPrepare
                    || CurrentState == CreatureProceduralActionState.AttackWindup;
                if (recoverySlotsUsed > 0 && !leg.emergencySwing)
                    leg.stance = true;
                if (leg.emergencySwing)
                {
                    leg.stance = false;
                    leg.trajectoryMode = "Emergency";
                    leg.emergencySwingProgress = Mathf.Clamp01(
                        leg.emergencySwingProgress + Time.deltaTime / 0.22f);
                    target = EvaluateSwing(
                        leg.swingStart, leg.swingTarget, up,
                        profile.NormalizedStepHeight * rigScale,
                        leg.emergencySwingProgress);
                    targetNormal = Vector3.Slerp(
                        leg.plantedNormal, leg.swingTargetNormal,
                        leg.emergencySwingProgress);
                    if (leg.emergencySwingProgress >= 1f)
                    {
                        leg.emergencySwing = false;
                        leg.stance = true;
                        leg.plantedPosition = leg.swingTarget;
                        leg.plantedNormal = leg.swingTargetNormal;
                        target = leg.plantedPosition;
                        targetNormal = leg.plantedNormal;
                    }
                }
                else
                {
                    float reachLimit = leg.totalLength * 0.96f;
                    if (leg.stance && moving && recoverySlotsUsed == 0
                        && Vector3.Distance(leg.upper.position, leg.plantedPosition) > reachLimit)
                    {
                        leg.emergencySwing = true;
                        recoverySlotsUsed++;
                        leg.emergencySwingProgress = 0f;
                        leg.stance = false;
                        leg.swingStart = leg.foot.position;
                        PlanFoot(leg, up, forward, true,
                            out leg.swingTarget, out leg.swingTargetNormal);
                    }
                    else if (leg.stance && !leg.previousStance)
                    {
                        leg.plantedPosition = leg.swingTarget;
                        leg.plantedNormal = leg.swingTargetNormal;
                    }
                    else if (!leg.stance && leg.previousStance)
                    {
                        leg.swingStart = leg.foot.position;
                        PlanFoot(leg, up, forward, false,
                            out leg.swingTarget, out leg.swingTargetNormal);
                    }

                    if (leg.emergencySwing)
                    {
                        leg.trajectoryMode = "Emergency";
                        target = leg.swingStart;
                        targetNormal = leg.plantedNormal;
                    }
                    else if (leg.stance)
                    {
                        target = leg.plantedPosition;
                        targetNormal = leg.plantedNormal;
                    }
                    else
                    {
                        float swing01 = sampledSwingProgress[i];
                        leg.trajectoryMode = "Template";
                        target = EvaluateTemplateTrajectory(
                            leg, sampledFootOffsets[i], swing01,
                            up, forward,
                            out leg.strideScale, out leg.trajectoryResidual);
                        targetNormal = Vector3.Slerp(
                            leg.plantedNormal, leg.swingTargetNormal, swing01);
                    }
                }
            }
            SolveThreeSegmentLeg(leg, target, targetNormal, up);
            poleWeightSum += leg.poleTemplateWeight;
            MaxKneePoleDelta = Mathf.Max(
                MaxKneePoleDelta, leg.firstPoleAngularDelta);
            MaxHockPoleDelta = Mathf.Max(
                MaxHockPoleDelta, leg.secondPoleAngularDelta);
            MaxTemplateTrajectoryResidual = Mathf.Max(
                MaxTemplateTrajectoryResidual, leg.trajectoryResidual);
            if (Mathf.Abs(leg.strideScale - 1f) > Mathf.Abs(TemplateStrideScale - 1f))
                TemplateStrideScale = leg.strideScale;
            AllLegsIkReachable &= leg.ikReachable;
            if (leg.trajectoryMode == "Emergency")
                TrajectoryMode = "Emergency";
            else if (leg.trajectoryMode == "Template" && TrajectoryMode != "Emergency")
                TrajectoryMode = "Template";
            if (PoleFallbackReason == "None" && leg.poleFallbackReason != "None")
                PoleFallbackReason = leg.poleFallbackReason;
            float stanceError = Vector3.Distance(leg.foot.position, target);
            if (leg.stance)
            {
                maxStanceError = Mathf.Max(maxStanceError, stanceError);
                if (stanceError <= Mathf.Max(0.04f, rigScale * 0.065f))
                    contacts++;
            }
            if (leg.emergencySwing)
                reachRecoveries++;
            if (!leg.stance)
                stateMask |= 1 << i;
            maxSegmentError = Mathf.Max(
                maxSegmentError,
                Mathf.Abs(Vector3.Distance(leg.upper.position, leg.lower.position)
                    - leg.upperLength),
                Mathf.Abs(Vector3.Distance(leg.lower.position, leg.distal.position)
                    - leg.lowerLength),
                Mathf.Abs(Vector3.Distance(leg.distal.position, leg.foot.position)
                    - leg.distalLength));
            leg.previousStance = leg.stance;
        }
        ContactFootCount = contacts;
        IsStable = IsGrounded && contacts >= (moving ? 2 : 3);
        MaxStanceFootError = maxStanceError;
        MaxSegmentLengthError = maxSegmentError;
        ReachRecoveryLegCount = reachRecoveries;
        LegStateSummary = LegStateLabels[stateMask];
        PoleTemplateWeight = legs.Count > 0 ? poleWeightSum / legs.Count : 0f;
    }

    void EnsureMinimumTemplateSupport(bool moving)
    {
        if (!moving)
            return;
        int support = 0;
        for (int i = 0; i < legs.Count; i++)
            if (sampledStance[i] >= 0.5f)
                support++;
        while (support < 2)
        {
            int best = -1;
            float bestWeight = float.NegativeInfinity;
            for (int i = 0; i < legs.Count; i++)
            {
                if (sampledStance[i] >= 0.5f || sampledStance[i] <= bestWeight)
                    continue;
                best = i;
                bestWeight = sampledStance[i];
            }
            if (best < 0)
                break;
            sampledStance[best] = 1f;
            support++;
        }
    }

    void PlanFoot(
        LegRuntime leg, Vector3 up, Vector3 forward, bool recoverReach,
        out Vector3 target, out Vector3 normal)
    {
        Vector3 localGroundOffset = Vector3.ProjectOnPlane(leg.restGroundOffsetLocal, Vector3.up);
        Vector3 groundOffset = transform.TransformVector(localGroundOffset);
        Vector3 velocity = Vector3.ProjectOnPlane(body.velocity, up);
        Vector3 correction = (DesiredVelocity - velocity) * 0.12f;
        Vector3 prediction = velocity * Mathf.Lerp(0.12f, 0.22f,
            Mathf.Clamp01(DesiredVelocity.magnitude / Mathf.Max(0.1f, family.WalkSpeed)));
        Vector3 candidate = leg.upper.position + groundOffset + prediction + correction;
        if (!recoverReach)
        {
            Vector3 maxStep = Vector3.ClampMagnitude(
                Vector3.ProjectOnPlane(candidate - leg.plantedPosition, up),
                profile.NormalizedStepLength * rigScale);
            candidate = leg.plantedPosition + maxStep;
        }
        GroundFoot(candidate, up, leg.totalLength, leg.footPivotClearance,
            out target, out normal);
    }

    void GroundFoot(
        Vector3 candidate, Vector3 up, float reach, float pivotClearance,
        out Vector3 position, out Vector3 normal)
    {
        float radius = Mathf.Max(0.015f, rigScale * 0.025f);
        Vector3 origin = candidate + up * Mathf.Max(0.25f, reach * 0.55f);
        float distance = Mathf.Max(1.4f, reach * 2.2f);
        int count = Physics.SphereCastNonAlloc(
            origin, radius, -up, groundHits, distance,
            groundLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        position = candidate;
        normal = up;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider == null || hit.collider.attachedRigidbody == body
                || hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            position = hit.point + hit.normal * (
                pivotClearance + Mathf.Max(0.008f, rigScale * 0.006f));
            normal = hit.normal;
        }
    }

    void SolveThreeSegmentLeg(
        LegRuntime leg, Vector3 footTarget, Vector3 footNormal, Vector3 up)
    {
        Vector3 p0 = leg.upper.position;
        Vector3 p1 = leg.lower.position;
        Vector3 p2 = leg.distal.position;
        Vector3 p3 = leg.foot.position;
        Vector3 requested = footTarget - p0;
        float requestedDistance = requested.magnitude;
        if (requestedDistance < 0.0001f)
            return;

        float longest = Mathf.Max(
            leg.upperLength, Mathf.Max(leg.lowerLength, leg.distalLength));
        float minimumReach = Mathf.Max(
            0.01f,
            longest - (leg.totalLength - longest) + 0.005f);
        float maximumReach = leg.totalLength * 0.985f;
        leg.ikReachable = requestedDistance <= maximumReach + rigScale * 0.002f;
        float solvedDistance = Mathf.Clamp(
            requestedDistance, minimumReach, maximumReach);
        Vector3 solvedTarget = p0 + requested / requestedDistance * solvedDistance;
        if (leg.stance && requestedDistance > maximumReach)
            leg.plantedPosition = Vector3.Lerp(
                leg.plantedPosition, solvedTarget, 0.35f);

        Vector3 chainAxis = (solvedTarget - p0).normalized;
        ResolveDynamicPoleDirections(
            leg, chainAxis, up, requestedDistance / leg.totalLength,
            out Vector3 firstBend, out Vector3 secondBend);
        Vector3 firstPole = p0 + firstBend * leg.totalLength;

        for (int iteration = 0; iteration < 8; iteration++)
        {
            p3 = solvedTarget;
            p2 = p3 + SafeDirection(p2 - p3, -chainAxis) * leg.distalLength;
            p1 = p2 + SafeDirection(p1 - p2, -chainAxis) * leg.lowerLength;

            p1 = p0 + SafeDirection(p1 - p0, chainAxis) * leg.upperLength;
            p2 = p1 + SafeDirection(p2 - p1, chainAxis) * leg.lowerLength;
            p3 = p2 + SafeDirection(p3 - p2, chainAxis) * leg.distalLength;

            ApplyPoleConstraint(p0, p2, ref p1, firstPole);
            Vector3 secondPole = p1 + secondBend
                * (leg.lowerLength + leg.distalLength);
            ApplyPoleConstraint(p1, p3, ref p2, secondPole);
        }

        RotateBoneToward(leg.upper, leg.lower.position - leg.upper.position,
            p1 - p0, 115f);
        RotateBoneToward(leg.lower, leg.distal.position - leg.lower.position,
            p2 - p1, 125f);
        RotateBoneToward(leg.distal, leg.foot.position - leg.distal.position,
            p3 - p2, 115f);
        if (footNormal.sqrMagnitude > 0.001f)
            RotateBoneToward(leg.foot, leg.foot.up, footNormal.normalized, 24f);
    }

    void ResolveDynamicPoleDirections(
        LegRuntime leg,
        Vector3 chainAxis,
        Vector3 up,
        float reachRatio,
        out Vector3 firstBend,
        out Vector3 secondBend)
    {
        leg.poleFallbackReason = "None";
        Vector3 restFirst = ProjectBendDirection(
            transform.TransformDirection(leg.restFirstBendRootLocal), chainAxis);
        Vector3 restSecond = ProjectBendDirection(
            transform.TransformDirection(leg.restSecondBendRootLocal), chainAxis);
        bool firstValid = TryProjectBendDirection(
            transform.TransformDirection(leg.templateFirstPoleRootLocal),
            chainAxis, out Vector3 templateFirst);
        bool secondValid = TryProjectBendDirection(
            transform.TransformDirection(leg.templateSecondPoleRootLocal),
            chainAxis, out Vector3 templateSecond);
        if (!firstValid)
        {
            templateFirst = restFirst;
            leg.poleFallbackReason = "KneeSingular";
        }
        if (!secondValid)
        {
            templateSecond = restSecond;
            leg.poleFallbackReason = leg.poleFallbackReason == "None"
                ? "HockSingular" : "BothSingular";
        }

        float slope = Vector3.Angle(up, groundNormal);
        float terrainStress = Mathf.InverseLerp(5f, 25f, slope);
        float reachStress = Mathf.InverseLerp(0.90f, 0.985f, reachRatio);
        float adaptation = Mathf.Max(terrainStress, reachStress);
        float templateWeight = Mathf.Lerp(0.90f, 0.45f, adaptation) * gaitBlend;
        if (leg.emergencySwing || CurrentState == CreatureProceduralActionState.Airborne
            || !firstValid || !secondValid)
            templateWeight = 0f;
        leg.poleTemplateWeight = templateWeight;

        if (!leg.poleInitialized)
        {
            leg.currentFirstPoleWorld = restFirst;
            leg.currentSecondPoleWorld = restSecond;
            leg.poleInitialized = true;
        }
        templateFirst = AlignHemisphere(templateFirst, leg.currentFirstPoleWorld);
        templateSecond = AlignHemisphere(templateSecond, leg.currentSecondPoleWorld);
        Vector3 desiredFirst = Vector3.Slerp(restFirst, templateFirst, templateWeight);
        Vector3 desiredSecond = Vector3.Slerp(restSecond, templateSecond, templateWeight);
        desiredFirst = AlignHemisphere(desiredFirst, leg.currentFirstPoleWorld);
        desiredSecond = AlignHemisphere(desiredSecond, leg.currentSecondPoleWorld);

        float maximumRadians = 540f * Mathf.Deg2Rad
            * Mathf.Max(Time.deltaTime, 1f / 240f);
        Vector3 previousFirst = leg.currentFirstPoleWorld;
        Vector3 previousSecond = leg.currentSecondPoleWorld;
        leg.currentFirstPoleWorld = Vector3.RotateTowards(
            previousFirst, desiredFirst, maximumRadians, 0f).normalized;
        leg.currentSecondPoleWorld = Vector3.RotateTowards(
            previousSecond, desiredSecond, maximumRadians, 0f).normalized;
        leg.firstPoleAngularDelta = Vector3.Angle(
            previousFirst, leg.currentFirstPoleWorld);
        leg.secondPoleAngularDelta = Vector3.Angle(
            previousSecond, leg.currentSecondPoleWorld);
        firstBend = ProjectBendDirection(leg.currentFirstPoleWorld, chainAxis);
        secondBend = ProjectBendDirection(leg.currentSecondPoleWorld, chainAxis);
    }

    static Vector3 SafeDirection(Vector3 value, Vector3 fallback)
    {
        return value.sqrMagnitude > 0.000001f
            ? value.normalized
            : fallback.normalized;
    }

    static Vector3 RestBendDirection(
        Vector3 jointFromRoot, Vector3 chainAxis, Vector3 fallback)
    {
        Vector3 bend = Vector3.ProjectOnPlane(jointFromRoot, chainAxis);
        if (bend.sqrMagnitude < 0.000001f)
            bend = Vector3.ProjectOnPlane(fallback, chainAxis);
        if (bend.sqrMagnitude < 0.000001f)
            bend = Vector3.Cross(chainAxis.normalized, Vector3.right);
        return bend.normalized;
    }

    static Vector3 ProjectBendDirection(Vector3 bend, Vector3 chainAxis)
    {
        Vector3 projected = Vector3.ProjectOnPlane(bend, chainAxis);
        if (projected.sqrMagnitude < 0.000001f)
            projected = Vector3.Cross(chainAxis, Vector3.right);
        if (projected.sqrMagnitude < 0.000001f)
            projected = Vector3.Cross(chainAxis, Vector3.forward);
        return projected.normalized;
    }

    static bool TryProjectBendDirection(
        Vector3 bend, Vector3 chainAxis, out Vector3 projected)
    {
        projected = Vector3.ProjectOnPlane(bend, chainAxis);
        if (projected.sqrMagnitude < 0.000001f
            || float.IsNaN(projected.x) || float.IsInfinity(projected.x)
            || float.IsNaN(projected.y) || float.IsInfinity(projected.y)
            || float.IsNaN(projected.z) || float.IsInfinity(projected.z))
        {
            projected = Vector3.zero;
            return false;
        }
        projected.Normalize();
        return true;
    }

    static Vector3 AlignHemisphere(Vector3 value, Vector3 reference)
    {
        return Vector3.Dot(value, reference) < 0f ? -value : value;
    }

    static void ApplyPoleConstraint(
        Vector3 previous, Vector3 next, ref Vector3 joint, Vector3 pole)
    {
        Vector3 axis = next - previous;
        if (axis.sqrMagnitude < 0.000001f)
            return;
        axis.Normalize();
        Vector3 jointRadial = Vector3.ProjectOnPlane(joint - previous, axis);
        Vector3 poleRadial = Vector3.ProjectOnPlane(pole - previous, axis);
        if (jointRadial.sqrMagnitude < 0.000001f
            || poleRadial.sqrMagnitude < 0.000001f)
            return;
        float angle = Vector3.SignedAngle(jointRadial, poleRadial, axis);
        joint = previous + Quaternion.AngleAxis(angle, axis) * (joint - previous);
    }

    void ApplyFullBodyMotion(Vector3 up)
    {
        float time = Time.time;
        float motionSpeed = Mathf.Max(CurrentSpeed, DesiredVelocity.magnitude * 0.35f);
        float speed01 = Mathf.Clamp01(motionSpeed / Mathf.Max(0.1f, family.WalkSpeed));
        float templateWeight = gaitBlend * speed01;
        currentGaitLift = 0f;
        for (int i = 0; i < bodyTracks.Count; i++)
        {
            BoneTrackRuntime runtime = bodyTracks[i];
            runtime.track.Sample(gaitPhase, out Vector3 normalizedPosition,
                out Quaternion rotationOffset);
            float positionLimit = runtime.bone == bodyBone ? 0.08f : 0.025f;
            Vector3 positionOffset = Vector3.ClampMagnitude(
                normalizedPosition, positionLimit) * rigScale * templateWeight;
            runtime.bone.localPosition = runtime.restPosition + positionOffset;
            runtime.bone.localRotation = runtime.restRotation * Quaternion.Slerp(
                Quaternion.identity, rotationOffset, templateWeight);
            if (runtime.bone == bodyBone)
                currentGaitLift = Mathf.Clamp(
                    Vector3.Dot(transform.TransformVector(positionOffset), up),
                    -rigScale * 0.04f, rigScale * 0.04f);
        }

        float breathe = Mathf.Sin(time * 2.1f)
            * Mathf.Lerp(0.8f, 0.2f, speed01);
        bodyBone.localRotation *= Quaternion.Euler(breathe, 0f, 0f);
        if (neckBone != null)
        {
            float pitch = CurrentState == CreatureProceduralActionState.AttackWindup ? -14f
                : CurrentState == CreatureProceduralActionState.AttackStrike ? 22f
                : Mathf.Sin(time * 0.7f) * 2.2f * (1f - templateWeight);
            neckBone.localRotation *= Quaternion.Euler(pitch, 0f, 0f);
        }
        if (headBone != null)
        {
            float yaw = CurrentState == CreatureProceduralActionState.Idle
                ? Mathf.Sin(time * 0.43f) * 8f : 0f;
            headBone.localRotation *= Quaternion.Euler(0f, yaw, 0f);
        }
        if (tailBone != null)
        {
            float yaw = Mathf.Sin(time * Mathf.Lerp(1.2f, 3.2f, speed01))
                * Mathf.Lerp(5f, 8f, speed01) * (1f - templateWeight * 0.65f);
            tailBone.localRotation *= Quaternion.Euler(0f, yaw, 0f);
        }
    }

    void UpdateAttackImpact(Vector3 up)
    {
        if (CurrentState != CreatureProceduralActionState.AttackStrike
            || attackImpactSent || headBone == null)
            return;
        float radius = Mathf.Max(0.08f, rigScale * 0.09f);
        if (!Physics.SphereCast(
            headBone.position, radius, surfaceForward, out RaycastHit hit,
            Mathf.Max(0.25f, rigScale * 0.35f), ~0, QueryTriggerInteraction.Ignore)
            || hit.collider == capsule)
            return;
        attackImpactSent = true;
        OnAttackImpact?.Invoke(new CreatureAttackImpact
        {
            position = hit.point,
            direction = surfaceForward,
            impulse = profile.AttackImpulse
        });
    }

    bool ProbeBodyGround(
        Vector3 up, out RaycastHit nearestHit, out float bodyHeight)
    {
        float radius = Mathf.Max(0.025f, capsule.radius * 0.35f);
        Vector3 reference = bodyBone != null ? bodyBone.position : capsule.bounds.center;
        Vector3 origin = reference + up * Mathf.Max(0.08f, rigScale * 0.12f);
        float distance = Mathf.Max(0.5f, restBodyHeight + rigScale * 0.65f);
        int count = Physics.SphereCastNonAlloc(
            origin, radius, -up, groundHits, distance,
            groundLayers, QueryTriggerInteraction.Ignore);
        nearestHit = default;
        bodyHeight = currentBodyHeight > 0f ? currentBodyHeight : restBodyHeight;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider == null || hit.collider == capsule
                || hit.collider.attachedRigidbody == body || hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            nearestHit = hit;
        }
        if (nearest >= distance)
            return false;
        bodyHeight = Vector3.Dot(reference - nearestHit.point, up);
        float captureHeight = restBodyHeight + rigScale * 0.2f;
        return bodyHeight <= captureHeight;
    }

    void ConfigureCollider()
    {
        int forwardAxis = LargestAxis(localForwardAxis);
        int lateralAxis = forwardAxis == 0 ? 2 : 0;
        float minForward = float.PositiveInfinity;
        float maxForward = float.NegativeInfinity;
        float minLateral = float.PositiveInfinity;
        float maxLateral = float.NegativeInfinity;
        Vector3 center = Vector3.zero;
        for (int i = 0; i < legs.Count; i++)
        {
            Vector3 upper = transform.InverseTransformPoint(legs[i].upper.position);
            center += upper;
            float along = GetAxis(upper, forwardAxis);
            float lateral = GetAxis(upper, lateralAxis);
            minForward = Mathf.Min(minForward, along);
            maxForward = Mathf.Max(maxForward, along);
            minLateral = Mathf.Min(minLateral, lateral);
            maxLateral = Mathf.Max(maxLateral, lateral);
        }
        center /= legs.Count;
        SetAxis(ref center, forwardAxis, (minForward + maxForward) * 0.5f);
        center += Vector3.up * rigScale * 0.035f;
        float radius = Mathf.Max(rigScale * 0.07f,
            (maxLateral - minLateral) * profile.ColliderRadiusScale);
        float height = Mathf.Max(radius * 2f,
            maxForward - minForward + radius * 1.5f);
        capsule.direction = forwardAxis;
        capsule.radius = radius;
        capsule.height = height;
        capsule.center = center;
        bodyMaterial = new PhysicMaterial("NMS Procedural Torso")
        {
            bounciness = 0f,
            dynamicFriction = 0.35f,
            staticFriction = 0.45f,
            bounceCombine = PhysicMaterialCombine.Minimum,
            frictionCombine = PhysicMaterialCombine.Average
        };
        capsule.sharedMaterial = bodyMaterial;
        body.centerOfMass = capsule.center;
    }

    static int LargestAxis(Vector3 value)
    {
        value = new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        if (value.x >= value.y && value.x >= value.z) return 0;
        return value.y >= value.z ? 1 : 2;
    }

    static float GetAxis(Vector3 value, int axis)
    {
        return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
    }

    static void SetAxis(ref Vector3 value, int axis, float component)
    {
        if (axis == 0) value.x = component;
        else if (axis == 1) value.y = component;
        else value.z = component;
    }

    void OnDestroy()
    {
        if (bodyMaterial == null)
            return;
        if (Application.isPlaying)
            Destroy(bodyMaterial);
        else
            DestroyImmediate(bodyMaterial);
    }

    void ResetControlledPose()
    {
        for (int i = 0; i < legs.Count; i++)
        {
            LegRuntime leg = legs[i];
            leg.upper.localRotation = leg.upperRest;
            leg.lower.localRotation = leg.lowerRest;
            leg.distal.localRotation = leg.distalRest;
            leg.foot.localRotation = leg.footRest;
        }
        for (int i = 0; i < bodyTracks.Count; i++)
        {
            BoneTrackRuntime runtime = bodyTracks[i];
            runtime.bone.localPosition = runtime.restPosition;
            runtime.bone.localRotation = runtime.restRotation;
        }
        if (bodyBone != null && bodyTracks.Count == 0)
        {
            bodyBone.localRotation = bodyRest;
            bodyBone.localPosition = bodyRestPosition;
        }
    }

    static Vector3 EvaluateSwing(
        Vector3 start, Vector3 end, Vector3 up, float height, float t)
    {
        t = Mathf.Clamp01(t);
        Vector3 c1 = start + up * height;
        Vector3 c2 = end + up * height;
        float u = 1f - t;
        return u * u * u * start + 3f * u * u * t * c1
            + 3f * u * t * t * c2 + t * t * t * end;
    }

    Vector3 EvaluateTemplateTrajectory(
        LegRuntime leg,
        Vector3 normalizedFootOffset,
        float progress,
        Vector3 up,
        Vector3 forward,
        out float strideScale,
        out float residualMagnitude)
    {
        progress = Mathf.Clamp01(progress);
        Vector3 sourceStart = leg.gaitTrack.NormalizedLiftOffOffset;
        Vector3 sourceEnd = leg.gaitTrack.NormalizedTouchDownOffset;
        Vector3 sourceBaseline = Vector3.Lerp(sourceStart, sourceEnd, progress);
        Vector3 sourceResidual = normalizedFootOffset - sourceBaseline;

        Vector3 sourceForward = Vector3.ProjectOnPlane(localForwardAxis, Vector3.up);
        if (sourceForward.sqrMagnitude < 0.000001f)
            sourceForward = Vector3.forward;
        sourceForward.Normalize();
        Vector3 sourceRight = Vector3.Cross(Vector3.up, sourceForward).normalized;
        Vector3 runtimeForward = Vector3.ProjectOnPlane(forward, up).normalized;
        if (runtimeForward.sqrMagnitude < 0.000001f)
            runtimeForward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        Vector3 runtimeRight = Vector3.Cross(up, runtimeForward).normalized;

        float sourceStride = Vector3.ProjectOnPlane(
            sourceEnd - sourceStart, Vector3.up).magnitude * rigScale;
        float actualStride = Vector3.ProjectOnPlane(
            leg.swingTarget - leg.swingStart, up).magnitude;
        strideScale = sourceStride > rigScale * 0.005f
            ? Mathf.Clamp(actualStride / sourceStride, 0.65f, 1.35f)
            : 1f;
        float lateralScale = Mathf.Clamp(strideScale, 0.8f, 1.2f);
        float forwardResidual = Vector3.Dot(sourceResidual, sourceForward)
            * rigScale * strideScale;
        float lateralResidual = Mathf.Clamp(
            Vector3.Dot(sourceResidual, sourceRight) * rigScale * lateralScale,
            -leg.totalLength * 0.10f,
            leg.totalLength * 0.10f);
        float verticalResidual = Mathf.Clamp(
            sourceResidual.y * rigScale,
            -leg.totalLength * 0.08f,
            leg.totalLength * 0.25f);
        Vector3 mappedResidual = runtimeForward * forwardResidual
            + runtimeRight * lateralResidual
            + up * verticalResidual;
        residualMagnitude = mappedResidual.magnitude;
        return Vector3.Lerp(leg.swingStart, leg.swingTarget, progress)
            + mappedResidual;
    }

    static void RotateBoneToward(
        Transform bone, Vector3 current, Vector3 desired, float maximumDegrees)
    {
        if (current.sqrMagnitude < 0.000001f || desired.sqrMagnitude < 0.000001f)
            return;
        Quaternion correction = Quaternion.FromToRotation(current, desired);
        correction.ToAngleAxis(out float angle, out Vector3 axis);
        if (float.IsNaN(angle) || float.IsInfinity(angle)
            || axis.sqrMagnitude < 0.000001f)
            return;
        if (angle > 180f)
            angle -= 360f;
        angle = Mathf.Clamp(angle, -maximumDegrees, maximumDegrees);
        bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation;
    }

    static Transform FindBone(Dictionary<string, Transform> bones, string name)
    {
        return !string.IsNullOrEmpty(name) && bones.TryGetValue(name, out Transform bone)
            ? bone : null;
    }

    static bool FinitePositive(float value)
    {
        return value > 0.000001f && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    static Quaternion RotationForLocalForward(
        Vector3 localForward, Vector3 worldForward, Vector3 worldUp)
    {
        Vector3 axis = localForward.sqrMagnitude > 0.000001f
            ? localForward.normalized : Vector3.forward;
        Quaternion localBasis = Quaternion.LookRotation(axis, Vector3.up);
        return Quaternion.LookRotation(worldForward, worldUp) * Quaternion.Inverse(localBasis);
    }

}
