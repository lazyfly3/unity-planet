using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class ImportedLegIKState
{
    public Vector3 plantedPosition;
    public Vector3 swingStart;
    public Vector3 swingTarget;
    public Vector3 groundNormal = Vector3.up;
    public float swingStartedAt;
    public float swingDuration;
    public float plantedSince;
    public bool initialized;
    public bool swinging;
    public bool wasInSwingWindow;
}

[DisallowMultipleComponent]
public sealed class ImportedCreatureSemanticAnimator : MonoBehaviour
{
    sealed class LegChain
    {
        public CreatureSemanticLegDefinition definition;
        public Transform upper;
        public Transform lower;
        public Transform ankle;
        public Transform foot;
        public Quaternion upperRest;
        public Quaternion lowerRest;
        public Quaternion ankleRest;
        public Quaternion footRest;
        public Vector3 upperRestPosition;
        public Vector3 lowerRestPosition;
        public Vector3 ankleRestPosition;
        public Vector3 footRestPosition;
        public Vector3 upperRestScale;
        public Vector3 lowerRestScale;
        public Vector3 ankleRestScale;
        public Vector3 footRestScale;
        public Quaternion footFrameOffset;
        public Vector3 upperAxisLocal;
        public Vector3 lowerAxisLocal;
        public Vector3 ankleAxisLocal;
        public Vector3 ankleDirectionRootLocal;
        public Vector3 bendHintRootLocal;
        public float upperLength;
        public float lowerLength;
        public float ankleLength;
        public float restForward;
        public float restLateral;
        public float restReach;
        public float profileScale = 1f;
        public readonly ImportedLegIKState state = new ImportedLegIKState();

        public float TotalLength => upperLength + lowerLength + ankleLength;
        public bool IsValid => upper != null && lower != null && ankle != null && foot != null
            && lower.parent == upper && ankle.parent == lower && foot.parent == ankle
            && upperLength > .001f && lowerLength > .001f && ankleLength > .001f;
    }

    readonly RaycastHit[] groundHits = new RaycastHit[24];
    readonly Dictionary<string, Transform> boneMap = new Dictionary<string, Transform>(StringComparer.Ordinal);
    static readonly ImportedFootContactProfile FallbackFootContact = new ImportedFootContactProfile();

    [SerializeField] bool logIKDiagnostics;

    Rigidbody body;
    CreatureSphereMotor motor;
    SphericalGravitySource gravitySource;
    LayerMask groundLayers;
    float gaitFrequency;
    float maximumSpeed = 7.2f;
    LegChain[] legs = Array.Empty<LegChain>();
    Transform pelvis;
    Transform neck;
    Transform head;
    Transform[] tail = Array.Empty<Transform>();
    Vector3 pelvisRestPosition;
    Quaternion pelvisRestRotation;
    Quaternion neckRest;
    Quaternion headRest;
    Quaternion[] tailRest = Array.Empty<Quaternion>();
    float nextDiagnosticTime;
    float pelvisDrop;
    float requestedPelvisDrop;
    float locomotionCycle;
    float currentGaitRate;

    public int ValidLegCount { get; private set; }
    public float TrotBlend { get; private set; }

    public bool Configure(Rigidbody targetBody, CreatureSphereMotor sphereMotor, float frequency)
    {
SphericalGravitySource source = FindObjectOfType<SphericalGravitySource>();
        return Configure(targetBody, sphereMotor, source, ~0, frequency, 7.2f, CreateDefaultSemantics());
}

    public bool Configure(
        Rigidbody targetBody,
        CreatureSphereMotor sphereMotor,
        SphericalGravitySource source,
        LayerMask layers,
        float frequency,
        float maxSpeed,
        CreatureRigSemantics semantics)
    {
body = targetBody;
        motor = sphereMotor;
        gravitySource = source;
        groundLayers = layers;
        gaitFrequency = Mathf.Clamp(frequency, .5f, 4f);
        maximumSpeed = Mathf.Max(.1f, maxSpeed);
        semantics = semantics ?? CreateDefaultSemantics();

        BuildBoneMap();
        int legCount = semantics.legs != null ? semantics.legs.Count : 0;
        legs = new LegChain[legCount];
        ValidLegCount = 0;
        for (int i = 0; i < legCount; i++)
        {
            legs[i] = CreateLeg(semantics.legs[i]);
            if (legs[i].IsValid)
                ValidLegCount++;
        }

        pelvis = FindBone(semantics.pelvisBone);
        neck = FindBone(semantics.neckBone);
        head = FindBone(semantics.headBone);
        if (pelvis != null)
        {
            pelvisRestPosition = pelvis.localPosition;
            pelvisRestRotation = pelvis.localRotation;
        }
        neckRest = neck != null ? neck.localRotation : Quaternion.identity;
        headRest = head != null ? head.localRotation : Quaternion.identity;

        tail = FindTailChain(semantics);
        tailRest = new Quaternion[tail.Length];
        for (int i = 0; i < tail.Length; i++)
            tailRest[i] = tail[i].localRotation;

        if (ValidLegCount != legCount)
            Debug.LogWarning($"Imported creature rig found {ValidLegCount}/{legCount} valid IK leg chains.", this);
        return ValidLegCount > 0 && gravitySource != null;
}

    void LateUpdate()
    {
        if (body == null || gravitySource == null || legs.Length == 0)
            return;

        Vector3 up = gravitySource.GetUp(body.position);
        Vector3 forward = Vector3.ProjectOnPlane(motor != null ? motor.SurfaceForward : transform.forward, up);
        if (forward.sqrMagnitude < .0001f)
            forward = Vector3.ProjectOnPlane(transform.forward, up);
        forward.Normalize();
        Vector3 right = Vector3.Cross(up, forward).normalized;
        float speed = Vector3.ProjectOnPlane(body.velocity, up).magnitude;
        float speed01 = Mathf.Clamp01(speed / maximumSpeed);

        // A ground IK target is meaningful only while the body is supported by the
        // planet. Solving toward the surface while airborne folds every leg toward
        // the planet center and was the source of the extreme "twisted" poses.
        if (motor == null || !motor.IsGrounded || speed < .08f)
        {
            locomotionCycle = 0f;
            requestedPelvisDrop = 0f;
            pelvisDrop = Mathf.MoveTowards(pelvisDrop, 0f, Time.deltaTime * 2f);
            if (pelvis != null)
            {
                pelvis.localPosition = pelvisRestPosition;
                pelvis.localRotation = pelvisRestRotation;
            }
            for (int i = 0; i < legs.Length; i++)
            {
                ResetLegToRest(legs[i]);
                if (legs[i] != null)
                {
                    legs[i].state.initialized = false;
                    legs[i].state.swinging = false;
                }
            }
            ApplySecondaryMotion(0f);
            return;
        }

        TrotBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.45f, .65f, speed01));
        float averageLegLength = GetAverageLegLength();
        float strideCapacity = Mathf.Max(.25f, averageLegLength * Mathf.Lerp(.42f, .9f, speed01));
        float speedMatchedRate = speed / strideCapacity;
        currentGaitRate = Mathf.Clamp(Mathf.Max(gaitFrequency, speedMatchedRate), .5f, 10f);
        locomotionCycle = Mathf.Repeat(locomotionCycle + currentGaitRate * Time.deltaTime, 1f);

        requestedPelvisDrop = 0f;
        ApplyBodyMotion(up, forward, speed01);
        for (int i = 0; i < legs.Length; i++)
            ResetLegToRest(legs[i]);
        for (int i = 0; i < legs.Length; i++)
            UpdateLeg(legs[i], up, forward, right, speed01);
        pelvisDrop = Mathf.MoveTowards(pelvisDrop, requestedPelvisDrop, Time.deltaTime * .8f);
        ApplySecondaryMotion(speed01);

        if (logIKDiagnostics && Time.time >= nextDiagnosticTime)
        {
            nextDiagnosticTime = Time.time + 3f;
            float radius = Vector3.Distance(body.position, gravitySource.Center);
            int planted = 0;
            float maximumFootDistance = 0f;
            for (int i = 0; i < legs.Length; i++)
            {
                if (legs[i] == null || !legs[i].state.initialized) continue;
                planted += legs[i].state.swinging ? 0 : 1;
                maximumFootDistance = Mathf.Max(maximumFootDistance,
                    Vector3.Distance(legs[i].upper.position, legs[i].state.plantedPosition));
            }
            Debug.Log($"Imported IK state: radius={radius:F2}, grounded={motor != null && motor.IsGrounded}, " +
                $"speed={speed:F2}, validLegs={ValidLegCount}, planted={planted}, " +
                $"maxFootReach={maximumFootDistance:F2}", this);
        }
    }

    void ApplyBodyMotion(Vector3 up, Vector3 forward, float speed01)
    {
        if (pelvis == null)
            return;
        float cycle = locomotionCycle * Mathf.PI * 2f;
        float bob = Mathf.Sin(cycle * (1f + TrotBlend)) * .025f * speed01;
        float pitch = Mathf.Sin(cycle) * 1.5f * speed01;
        Vector3 worldOffset = up * (bob - pelvisDrop);
        pelvis.localPosition = pelvisRestPosition
            + (pelvis.parent != null ? pelvis.parent.InverseTransformVector(worldOffset) : worldOffset);
        pelvis.localRotation = pelvisRestRotation * Quaternion.AngleAxis(pitch, Vector3.right);
    }

    void UpdateLeg(LegChain leg, Vector3 up, Vector3 forward, Vector3 right, float speed01)
    {
        if (leg == null || !leg.IsValid)
            return;

        Vector3 hip = leg.upper.position;
        float totalLength = leg.TotalLength;
        float stride = totalLength * Mathf.Lerp(.08f, .42f, speed01);
        float lateral = leg.restLateral + (leg.definition.isLeft ? -1f : 1f) * totalLength * .025f;
        float lead = leg.restForward + (leg.definition.isFront ? stride * .12f : -stride * .08f);
        Vector3 nominal = hip - up * totalLength * .93f + right * lateral + forward * lead;
        Vector3 desiredPivot = SampleFootFrame(leg, nominal, up, forward, right, out Vector3 groundNormal);
        Vector3 hipToDesired = desiredPivot - hip;
        float desiredReach = hipToDesired.magnitude;
        float maximumReach = totalLength * .97f;
        if (desiredReach > maximumReach && desiredReach > .0001f)
        {
            requestedPelvisDrop = Mathf.Max(
                requestedPelvisDrop,
                Mathf.Min(desiredReach - maximumReach, totalLength * .16f));
            desiredPivot = hip + hipToDesired * (maximumReach / desiredReach);
        }

        ImportedLegIKState state = leg.state;
        if (!state.initialized)
        {
            state.plantedPosition = desiredPivot;
            state.swingStart = desiredPivot;
            state.swingTarget = desiredPivot;
            state.groundNormal = groundNormal;
            state.initialized = true;
        }

        bool moving = speed01 > .025f;
        float phaseOffset = CircularLerp(leg.definition.walkPhase, leg.definition.trotPhase, TrotBlend);
        float phase = Mathf.Repeat(locomotionCycle + phaseOffset, 1f);
        float swingFraction = Mathf.Lerp(.22f, .38f, TrotBlend);
        bool inSwingWindow = phase < swingFraction;
        float plantedError = Vector3.Distance(state.plantedPosition, desiredPivot);
        float plantedReach = Vector3.Distance(hip, state.plantedPosition);
        float stableReach = Mathf.Clamp(leg.restReach * 1.12f, totalLength * .58f, totalLength * .88f);
        bool mustStep = plantedReach > stableReach * .94f;
        float speed = Mathf.Max(.01f, speed01 * maximumSpeed);
        float maximumSupportTime = Mathf.Clamp(totalLength * .2f / speed, .055f, .22f);
        bool supportExpired = Time.time - state.plantedSince > maximumSupportTime
            && plantedError > totalLength * .035f;

        if (!moving)
        {
            state.swinging = false;
            state.plantedPosition = Vector3.MoveTowards(
                state.plantedPosition, desiredPivot, totalLength * 2.5f * Time.deltaTime);
            state.groundNormal = Vector3.Slerp(state.groundNormal, groundNormal, 12f * Time.deltaTime).normalized;
            state.plantedSince = Time.time;
        }
        else if (!state.swinging
            && ((inSwingWindow && plantedError > totalLength * .05f) || mustStep || supportExpired)
            && CanBeginSwing(leg))
        {
            BeginSwing(leg, desiredPivot, groundNormal, speed01, swingFraction);
        }

        Vector3 footGoal;
        if (state.swinging)
        {
            float t = Mathf.Clamp01((Time.time - state.swingStartedAt) / Mathf.Max(.03f, state.swingDuration));
            float smooth = t * t * (3f - 2f * t);
            Vector3 pathUp = gravitySource.GetUp(Vector3.Lerp(state.swingStart, state.swingTarget, smooth));
            float lift = totalLength * Mathf.Lerp(.08f, .18f, speed01);
            footGoal = Vector3.Lerp(state.swingStart, state.swingTarget, smooth)
                + pathUp * (Mathf.Sin(t * Mathf.PI) * lift);
            if (t >= 1f)
            {
                state.swinging = false;
                state.plantedPosition = state.swingTarget;
                state.plantedSince = Time.time;
                footGoal = state.plantedPosition;
            }
        }
        else
        {
            footGoal = state.plantedPosition;
        }

        state.wasInSwingWindow = inSwingWindow;
        SolveContinuousLegGoal(leg, hip, footGoal, forward, up, state.groundNormal);
    }

    void BeginSwing(
        LegChain leg,
        Vector3 desiredPivot,
        Vector3 groundNormal,
        float speed01,
        float swingFraction)
    {
        ImportedLegIKState state = leg.state;
        state.swinging = true;
        state.swingStart = state.plantedPosition;
        state.swingStartedAt = Time.time;
        state.swingDuration = Mathf.Max(.075f, swingFraction / (currentGaitRate * Mathf.Lerp(.9f, 1.18f, speed01)));
        Vector3 tangentVelocity = Vector3.ProjectOnPlane(
            body.velocity, gravitySource.GetUp(body.position));
        state.swingTarget = desiredPivot + tangentVelocity * (state.swingDuration * .35f);
        state.swingTarget = SampleGroundPoint(
            state.swingTarget,
            gravitySource.GetUp(state.swingTarget),
            leg.TotalLength,
            out state.groundNormal);
        state.swingTarget += state.groundNormal
            * ((leg.definition.footContact ?? FallbackFootContact).soleOffset * leg.profileScale + .02f);
    }

    bool CanBeginSwing(LegChain candidate)
    {
        int swinging = 0;
        for (int i = 0; i < legs.Length; i++)
        {
            LegChain leg = legs[i];
            if (leg == null || !leg.state.swinging)
                continue;
            swinging++;
            if (TrotBlend > .5f
                && leg.definition.isLeft != candidate.definition.isLeft
                && leg.definition.isFront != candidate.definition.isFront)
                continue;
            if (TrotBlend > .5f)
                return false;
        }
        return swinging < (TrotBlend > .5f && legs.Length >= 4 ? 2 : 1);
    }

    Vector3 SampleFootFrame(
        LegChain leg,
        Vector3 candidate,
        Vector3 up,
        Vector3 forward,
        Vector3 right,
        out Vector3 groundNormal)
    {
        ImportedFootContactProfile profile = leg.definition.footContact ?? FallbackFootContact;
        float scale = leg.profileScale;
        float halfWidth = profile.halfWidth * scale;
        float heel = profile.heelDistance * scale;
        float toe = profile.toeDistance * scale;

        Vector3 heelPoint = SampleGroundPoint(candidate - forward * heel, up, leg.TotalLength, out Vector3 heelNormal);
        Vector3 toePoint = SampleGroundPoint(candidate + forward * toe, up, leg.TotalLength, out Vector3 toeNormal);
        Vector3 leftPoint = SampleGroundPoint(candidate - right * halfWidth, up, leg.TotalLength, out Vector3 leftNormal);
        Vector3 rightPoint = SampleGroundPoint(candidate + right * halfWidth, up, leg.TotalLength, out Vector3 rightNormal);
        Vector3 center = (heelPoint + toePoint + leftPoint + rightPoint) * .25f;
        Vector3 averageNormal = (heelNormal + toeNormal + leftNormal + rightNormal).normalized;
        Vector3 planeNormal = Vector3.Cross(rightPoint - leftPoint, toePoint - heelPoint);
        if (planeNormal.sqrMagnitude > .0001f)
        {
            planeNormal.Normalize();
            if (Vector3.Dot(planeNormal, up) < 0f)
                planeNormal = -planeNormal;
            averageNormal = Vector3.Slerp(averageNormal, planeNormal, .7f).normalized;
        }
        groundNormal = averageNormal.sqrMagnitude > .5f ? averageNormal : up;
        return center + groundNormal * (profile.soleOffset * scale + .02f);
    }

    Vector3 SampleGroundPoint(Vector3 candidate, Vector3 up, float legLength, out Vector3 normal)
    {
        Vector3 origin = candidate + up * Mathf.Max(1f, legLength * .7f);
        Vector3 down = -gravitySource.GetUp(origin);
        int count = Physics.RaycastNonAlloc(
            origin, down, groundHits, Mathf.Max(3f, legLength * 1.8f),
            groundLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        Vector3 point = gravitySource.GetSurfacePoint(candidate - gravitySource.Center);
        normal = gravitySource.GetUp(point);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider == null || hit.collider.attachedRigidbody == body || hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            point = hit.point;
            normal = hit.normal;
        }
        return point;
    }

    void SolveContinuousLegGoal(
        LegChain leg,
        Vector3 hip,
        Vector3 requestedFoot,
        Vector3 forward,
        Vector3 up,
        Vector3 groundNormal)
    {
        Vector3 toFoot = requestedFoot - hip;
        float requestedDistance = Mathf.Max(.001f, toFoot.magnitude);
        Vector3 direction = toFoot / requestedDistance;
        // Never solve near a straight line. Near full extension the analytic knee
        // plane becomes ill-conditioned and alternates sides from tiny target noise.
        float maximum = Mathf.Clamp(leg.restReach * 1.12f,
            leg.TotalLength * .58f, leg.TotalLength * .88f);
        float distance = Mathf.Min(requestedDistance, maximum);
        Vector3 footGoal = hip + direction * distance;

        // Preserve the terminal wrist/ankle segment from the imported Rest Pose. Its
        // target lets the proximal two joints use a stable hinge-plane solution.
        Vector3 terminalDirection = transform.TransformDirection(leg.ankleDirectionRootLocal);
        if (terminalDirection.sqrMagnitude < .0001f)
            terminalDirection = -up;
        terminalDirection.Normalize();
        Vector3 ankleGoal = footGoal - terminalDirection * leg.ankleLength;
        Vector3 hipToAnkle = ankleGoal - hip;
        float ankleDistance = Mathf.Max(.001f, hipToAnkle.magnitude);
        Vector3 proximalDirection = hipToAnkle / ankleDistance;
        float proximalMinimum = Mathf.Abs(leg.upperLength - leg.lowerLength) + .002f;
        float proximalMaximum = Mathf.Max(proximalMinimum, leg.upperLength + leg.lowerLength - .002f);
        ankleDistance = Mathf.Clamp(ankleDistance, proximalMinimum, proximalMaximum);
        ankleGoal = hip + proximalDirection * ankleDistance;

        float along = (leg.upperLength * leg.upperLength - leg.lowerLength * leg.lowerLength
            + ankleDistance * ankleDistance) / (2f * ankleDistance);
        float bendHeight = Mathf.Sqrt(Mathf.Max(0f,
            leg.upperLength * leg.upperLength - along * along));
        Vector3 bendHint = Vector3.ProjectOnPlane(
            transform.TransformDirection(leg.bendHintRootLocal), proximalDirection);
        if (bendHint.sqrMagnitude < .0001f)
            bendHint = Vector3.ProjectOnPlane(
                forward * (leg.definition.isFront ? 1f : -1f), proximalDirection);
        if (bendHint.sqrMagnitude < .0001f)
            bendHint = Vector3.ProjectOnPlane(up, proximalDirection);
        bendHint.Normalize();
        Vector3 kneeGoal = hip + proximalDirection * along + bendHint * bendHeight;

        RotateBoneAxisTo(leg.upper, leg.upperAxisLocal, kneeGoal - hip, leg.upperRest, 38f);
        RotateBoneAxisTo(leg.lower, leg.lowerAxisLocal, ankleGoal - leg.lower.position, leg.lowerRest, 58f);
        RotateBoneAxisTo(leg.ankle, leg.ankleAxisLocal, footGoal - leg.ankle.position, leg.ankleRest, 42f);

        Vector3 footUp = groundNormal.sqrMagnitude > .5f ? groundNormal.normalized : up;
        Vector3 footForward = Vector3.ProjectOnPlane(forward, footUp);
        if (footForward.sqrMagnitude < .0001f)
            footForward = Vector3.ProjectOnPlane(transform.forward, footUp);
        leg.foot.rotation = Quaternion.LookRotation(footForward.normalized, footUp) * leg.footFrameOffset;
        leg.foot.localRotation = ClampFromRest(leg.footRest, leg.foot.localRotation, 28f);
    }

    static void RotateBoneAxisTo(
        Transform bone,
        Vector3 localAxis,
        Vector3 worldDirection,
        Quaternion restLocalRotation,
        float maximumAngle)
    {
        if (bone == null || worldDirection.sqrMagnitude < .0001f)
            return;
        Vector3 axis = localAxis.sqrMagnitude > .0001f ? localAxis.normalized : Vector3.down;
        Vector3 current = bone.TransformDirection(axis);
        bone.rotation = Quaternion.FromToRotation(current, worldDirection.normalized) * bone.rotation;
        bone.localRotation = ClampFromRest(restLocalRotation, bone.localRotation, maximumAngle);
    }

    static Quaternion ClampFromRest(Quaternion rest, Quaternion candidate, float maximumAngle)
    {
        float angle = Quaternion.Angle(rest, candidate);
        if (angle <= maximumAngle || angle < .0001f)
            return candidate;
        return Quaternion.Slerp(rest, candidate, maximumAngle / angle);
    }

    LegChain CreateLeg(CreatureSemanticLegDefinition definition)
    {
        if (definition == null)
            return new LegChain();
        Transform upper = FindBone(definition.upperBone);
        Transform lower = FindBone(definition.lowerBone);
        Transform ankle = FindBone(definition.ankleBone);
        Transform foot = FindBone(definition.footBone);
        var leg = new LegChain
        {
            definition = definition,
            upper = upper,
            lower = lower,
            ankle = ankle,
            foot = foot,
            upperRest = upper != null ? upper.localRotation : Quaternion.identity,
            lowerRest = lower != null ? lower.localRotation : Quaternion.identity,
            ankleRest = ankle != null ? ankle.localRotation : Quaternion.identity,
            footRest = foot != null ? foot.localRotation : Quaternion.identity,
            upperRestPosition = upper != null ? upper.localPosition : Vector3.zero,
            lowerRestPosition = lower != null ? lower.localPosition : Vector3.zero,
            ankleRestPosition = ankle != null ? ankle.localPosition : Vector3.zero,
            footRestPosition = foot != null ? foot.localPosition : Vector3.zero,
            upperRestScale = upper != null ? upper.localScale : Vector3.one,
            lowerRestScale = lower != null ? lower.localScale : Vector3.one,
            ankleRestScale = ankle != null ? ankle.localScale : Vector3.one,
            footRestScale = foot != null ? foot.localScale : Vector3.one
        };
        if (upper == null || lower == null || ankle == null || foot == null)
            return leg;

        Vector3 upperSegment = lower.position - upper.position;
        Vector3 lowerSegment = ankle.position - lower.position;
        Vector3 ankleSegment = foot.position - ankle.position;
        leg.upperLength = Mathf.Max(.001f, upperSegment.magnitude);
        leg.lowerLength = Mathf.Max(.001f, lowerSegment.magnitude);
        leg.ankleLength = Mathf.Max(.001f, ankleSegment.magnitude);
        leg.upperAxisLocal = upper.InverseTransformDirection(upperSegment.normalized);
        leg.lowerAxisLocal = lower.InverseTransformDirection(lowerSegment.normalized);
        leg.ankleAxisLocal = ankle.InverseTransformDirection(ankleSegment.normalized);
        leg.ankleDirectionRootLocal = transform.InverseTransformDirection(ankleSegment.normalized);
        float catalogLength = Mathf.Max(.001f,
            definition.upperLength + definition.lowerLength + definition.ankleLength);
        leg.profileScale = leg.TotalLength / catalogLength;

        Vector3 line = foot.position - upper.position;
        Vector3 projected = upper.position + Vector3.Project(lower.position - upper.position, line);
        Vector3 bend = lower.position - projected;
        if (bend.sqrMagnitude < .0001f)
            bend = transform.TransformDirection(definition.bendHintRootLocal);
        leg.bendHintRootLocal = transform.InverseTransformDirection(bend.normalized);

        Vector3 rootUp = gravitySource != null ? gravitySource.GetUp(body.position) : transform.up;
        Vector3 rootForward = Vector3.ProjectOnPlane(transform.forward, rootUp).normalized;
        Vector3 rootRight = Vector3.Cross(rootUp, rootForward).normalized;
        Vector3 hipToFoot = foot.position - upper.position;
        leg.restReach = Mathf.Max(.001f, hipToFoot.magnitude);
        leg.restForward = Vector3.Dot(hipToFoot, rootForward);
        leg.restLateral = Vector3.Dot(hipToFoot, rootRight);
        Quaternion restFrame = Quaternion.LookRotation(rootForward, rootUp);
        leg.footFrameOffset = Quaternion.Inverse(restFrame) * foot.rotation;
        leg.state.plantedSince = Time.time;
        return leg;
    }

    void ResetLegToRest(LegChain leg)
    {
        if (leg == null)
            return;
        RestoreBone(leg.upper, leg.upperRestPosition, leg.upperRest, leg.upperRestScale);
        RestoreBone(leg.lower, leg.lowerRestPosition, leg.lowerRest, leg.lowerRestScale);
        RestoreBone(leg.ankle, leg.ankleRestPosition, leg.ankleRest, leg.ankleRestScale);
        RestoreBone(leg.foot, leg.footRestPosition, leg.footRest, leg.footRestScale);
    }

    static void RestoreBone(Transform bone, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        if (bone == null)
            return;
        bone.localPosition = position;
        bone.localRotation = rotation;
        bone.localScale = scale;
    }

    void ApplySecondaryMotion(float speed01)
    {
        float cycle = Time.time * gaitFrequency;
        float idle = Mathf.Sin(Time.time * 1.1f);
        float headPitch = Mathf.Sin(cycle * Mathf.PI * 4f) * -2f * speed01 + idle;
        if (neck != null)
            neck.localRotation = neckRest * Quaternion.AngleAxis(headPitch, Vector3.right);
        if (head != null)
            head.localRotation = headRest * Quaternion.AngleAxis(-headPitch * .5f, Vector3.right);
        float tailPhase = cycle * Mathf.PI * 2f;
        for (int i = 0; i < tail.Length; i++)
        {
            float angle = Mathf.Sin(tailPhase - i * .55f) * Mathf.Lerp(2f, 10f, speed01);
            tail[i].localRotation = tailRest[i] * Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }

    void BuildBoneMap()
    {
        boneMap.Clear();
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            if (!boneMap.ContainsKey(transforms[i].name))
                boneMap.Add(transforms[i].name, transforms[i]);
    }

    Transform FindBone(string boneName)
    {
        return !string.IsNullOrEmpty(boneName) && boneMap.TryGetValue(boneName, out Transform bone)
            ? bone : null;
    }

    Transform[] FindTailChain(CreatureRigSemantics semantics)
    {
        string[] longTail = semantics.longTailBones ?? Array.Empty<string>();
        string[] shortTail = semantics.shortTailBones ?? Array.Empty<string>();
        string[] names = longTail.Length > 0 && FindBone(longTail[0]) != null ? longTail : shortTail;
        var result = new List<Transform>(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            Transform bone = FindBone(names[i]);
            if (bone != null) result.Add(bone);
        }
        return result.ToArray();
    }

    static float CircularLerp(float from, float to, float t)
    {
        float delta = Mathf.DeltaAngle(from * 360f, to * 360f) / 360f;
        return Mathf.Repeat(from + delta * t, 1f);
    }

    float GetAverageLegLength()
    {
        float sum = 0f;
        int count = 0;
        for (int i = 0; i < legs.Length; i++)
        {
            if (legs[i] == null || !legs[i].IsValid)
                continue;
            sum += legs[i].TotalLength;
            count++;
        }
        return count > 0 ? sum / count : 1f;
    }

    static CreatureRigSemantics CreateDefaultSemantics()
    {
        var semantics = new CreatureRigSemantics();
        semantics.legs.Add(DefaultLeg("LF2ShoulderJNT", "LF2ElbowJNT", "LF2WristJNT", "LF2FootJNT", true, true, 0f, 0f));
        semantics.legs.Add(DefaultLeg("RF2ShoulderJNT", "RF2ElbowJNT", "RF2WristJNT", "RF2FootJNT", false, true, .5f, .5f));
        semantics.legs.Add(DefaultLeg("LBLegJNT", "LBKneeJNT", "LBAnkleJNT", "LBFootJNT", true, false, .75f, .5f));
        semantics.legs.Add(DefaultLeg("RBLegJNT", "RBKneeJNT", "RBAnkleJNT", "RBFootJNT", false, false, .25f, 0f));
        return semantics;
    }

    static CreatureSemanticLegDefinition DefaultLeg(
        string upper, string lower, string ankle, string foot,
        bool left, bool front, float walk, float trot)
    {
        return new CreatureSemanticLegDefinition
        {
            upperBone = upper,
            lowerBone = lower,
            ankleBone = ankle,
            footBone = foot,
            isLeft = left,
            isFront = front,
            walkPhase = walk,
            trotPhase = trot
        };
    }

    void OnDrawGizmosSelected()
    {
        for (int i = 0; i < legs.Length; i++)
        {
            LegChain leg = legs[i];
            if (leg == null || !leg.IsValid)
                continue;

            Gizmos.color = leg.definition.isFront
                ? new Color(.15f, .95f, .35f)
                : new Color(1f, .72f, .1f);
            Gizmos.DrawLine(leg.upper.position, leg.lower.position);
            Gizmos.DrawLine(leg.lower.position, leg.ankle.position);
            Gizmos.DrawLine(leg.ankle.position, leg.foot.position);
            Gizmos.DrawWireSphere(leg.upper.position, .035f);
            Gizmos.DrawWireSphere(leg.lower.position, .035f);
            Gizmos.DrawWireSphere(leg.ankle.position, .035f);
            Gizmos.DrawWireSphere(leg.foot.position, .035f);

            if (!leg.state.initialized)
                continue;
            Gizmos.color = leg.state.swinging ? Color.cyan : Color.green;
            Gizmos.DrawWireSphere(leg.state.plantedPosition, .08f);
            if (leg.state.swinging)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(leg.state.swingTarget, .07f);
            }
        }
    }
}
