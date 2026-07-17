using UnityEngine;

[DisallowMultipleComponent]
public sealed class CreatureSemanticAnimator : MonoBehaviour
{
    readonly RaycastHit[] groundHits = new RaycastHit[16];

    SphericalGravitySource gravitySource;
    Rigidbody body;
    CreatureGenome genome;
    CreatureRig rig;
    LayerMask groundLayers;
    Vector3 bodyRestPosition;
    Quaternion neckRestRotation;
    Quaternion headRestRotation;
    Quaternion tailRestRotation;
    SerpentineContactLocomotion serpentineLocomotion;

    public CreatureActionSemantic CurrentAction { get; private set; }

    public void Configure(
        SphericalGravitySource source,
        Rigidbody targetBody,
        CreatureGenome creatureGenome,
        CreatureRig creatureRig,
        LayerMask layers)
    {
gravitySource = source;
        body = targetBody;
        genome = creatureGenome;
        rig = creatureRig;
        groundLayers = layers;
        bodyRestPosition = rig.body.localPosition;
        neckRestRotation = rig.neck.localRotation;
        headRestRotation = rig.head.localRotation;
        tailRestRotation = rig.tailBase.localRotation;
        serpentineLocomotion = GetComponent<SerpentineContactLocomotion>();
    
}

    public void RefreshRestPose()
    {
        if (rig == null) return;
        bodyRestPosition = rig.body.localPosition;
        neckRestRotation = rig.neck.localRotation;
        headRestRotation = rig.head.localRotation;
        tailRestRotation = rig.tailBase.localRotation;
        if (rig.secondaryBones == null) return;
        foreach (CreatureSecondaryRig secondary in rig.secondaryBones)
            if (secondary != null && secondary.bone != null)
                secondary.restRotation = secondary.bone.localRotation;
    }

    void LateUpdate()
    {
        if (gravitySource == null || body == null || genome == null || rig == null)
            return;

        Vector3 up = gravitySource.GetUp(body.position);
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(Vector3.forward, up);
        forward.Normalize();
        Vector3 right = Vector3.Cross(up, forward).normalized;
        float surfaceSpeed = Vector3.ProjectOnPlane(body.velocity, up).magnitude;
        float speed01 = Mathf.Clamp01(surfaceSpeed / 8f);
        CurrentAction = genome.topology == CreatureTopology.Serpentine
            && serpentineLocomotion != null
            && serpentineLocomotion.LocomotionEnabled
                ? CreatureActionSemantic.Locomotion
                : (speed01 > 0.08f ? CreatureActionSemantic.Locomotion : CreatureActionSemantic.Idle);

        ApplyCorePose(speed01);
        ApplyLegGoals(up, forward, right, speed01);
        ApplySecondaryMotion(speed01);
    }

    void ApplySecondaryMotion(float speed01)
    {
        if (rig.secondaryBones == null)
            return;
        float phase = Time.time * genome.gaitFrequency * Mathf.PI * 2f;
        foreach (CreatureSecondaryRig secondary in rig.secondaryBones)
        {
            if (secondary.bone == null)
                continue;
            if (secondary.bone == rig.neck || secondary.bone == rig.head)
                continue;
            if (genome.topology == CreatureTopology.Serpentine
                && secondary.type == CreatureBodyNodeType.Tail)
                continue;

            float sideSign = secondary.side == CreatureBodySide.Left ? -1f
                : secondary.side == CreatureBodySide.Right ? 1f : 0f;
            float localPhase = phase + secondary.phase * Mathf.PI * 2f;
            float yaw = 0f;
            float pitch = 0f;
            switch (secondary.type)
            {
                case CreatureBodyNodeType.UpperArm:
                    pitch = Mathf.Sin(localPhase + sideSign * Mathf.PI * 0.5f) * Mathf.Lerp(5f, 28f, speed01);
                    break;
                case CreatureBodyNodeType.LowerArm:
                case CreatureBodyNodeType.Hand:
                    pitch = Mathf.Sin(localPhase + 0.8f) * Mathf.Lerp(4f, 16f, speed01);
                    break;
                case CreatureBodyNodeType.Tail:
                    yaw = Mathf.Sin(localPhase) * Mathf.Lerp(6f, 20f, speed01);
                    break;
                case CreatureBodyNodeType.Tentacle:
                    yaw = Mathf.Sin(localPhase * 0.77f) * 18f;
                    pitch = Mathf.Cos(localPhase * 0.61f) * 9f;
                    break;
                case CreatureBodyNodeType.Horn:
                case CreatureBodyNodeType.BackPlate:
                case CreatureBodyNodeType.Sensor:
                    yaw = Mathf.Sin(localPhase * 0.43f) * 2.5f;
                    break;
            }
            secondary.bone.localRotation = secondary.restRotation * Quaternion.Euler(pitch, yaw, 0f);
        }

        foreach (CreatureSecondaryRig secondary in rig.secondaryBones)
        {
            if (secondary.type != CreatureBodyNodeType.Neck && secondary.type != CreatureBodyNodeType.Head)
                continue;
            if (secondary.bone == rig.neck || secondary.bone == rig.head)
                continue;
            float lookMotion = Mathf.Sin(phase * 0.47f + secondary.phase * Mathf.PI * 2f) * 7f;
            secondary.bone.localRotation = secondary.restRotation * Quaternion.Euler(0f, lookMotion, 0f);
        }
    }

    void ApplyCorePose(float speed01)
    {
        float locomotionPhase = Time.time * genome.gaitFrequency * Mathf.PI * 2f;
        float idlePhase = Time.time * 1.35f;
        if (genome.topology == CreatureTopology.Serpentine)
        {
            float serpentinePhase = serpentineLocomotion != null ? serpentineLocomotion.WavePhase : locomotionPhase;
            ApplySerpentinePose(serpentinePhase, idlePhase);
            return;
        }

        float bodyBob = CurrentAction == CreatureActionSemantic.Locomotion
            ? Mathf.Sin(locomotionPhase * 2f) * genome.gaitHeight * 0.12f * speed01
            : Mathf.Sin(idlePhase) * 0.025f;
        float bodyRoll = CurrentAction == CreatureActionSemantic.Locomotion
            ? Mathf.Sin(locomotionPhase) * 2.5f * speed01
            : 0f;
        rig.body.localPosition = bodyRestPosition + Vector3.up * bodyBob;
        rig.body.localRotation = Quaternion.Euler(0f, 0f, bodyRoll);

        float headCounterMotion = CurrentAction == CreatureActionSemantic.Locomotion
            ? -Mathf.Sin(locomotionPhase * 2f) * 3f * speed01
            : Mathf.Sin(idlePhase * 0.7f) * 2f;
        rig.neck.localRotation = neckRestRotation * Quaternion.Euler(headCounterMotion, 0f, 0f);
        rig.head.localRotation = headRestRotation * Quaternion.Euler(-headCounterMotion * 0.65f, 0f, 0f);

        float tailYaw = Mathf.Sin(locomotionPhase + 0.8f) * Mathf.Lerp(4f, 13f, speed01);
        rig.tailBase.localRotation = tailRestRotation * Quaternion.Euler(0f, tailYaw, 0f);
    }

    void ApplySerpentinePose(float locomotionPhase, float idlePhase)
    {
        float amplitude = genome.serpentineWaveAmplitude;
        float phase = CurrentAction == CreatureActionSemantic.Locomotion ? locomotionPhase : idlePhase;
        float headCounterMotion = -Mathf.Sin(phase - rig.spineBones.Length * 0.72f) * amplitude * 0.35f;
        rig.neck.localRotation = neckRestRotation * Quaternion.Euler(0f, headCounterMotion, 0f);
        rig.head.localRotation = headRestRotation * Quaternion.Euler(0f, -headCounterMotion * 0.55f, 0f);
    }

    void ApplyLegGoals(Vector3 up, Vector3 forward, Vector3 right, float speed01)
    {
        float swingDuration = 0.34f;
        foreach (CreatureLegRig leg in rig.legs)
        {
            Vector3 hip = leg.upper.position;
            float stride = leg.TotalLength * Mathf.Lerp(0.12f, 0.42f, speed01);
            float cycle = Time.time * genome.gaitFrequency + leg.phaseOffset;
            float strideDirection = Mathf.Sin(cycle * Mathf.PI * 2f);
            Vector3 lateral = right * (leg.IsLeft ? -genome.footScale * 0.12f : genome.footScale * 0.12f);
            Vector3 candidate = hip - up * leg.TotalLength + forward * (strideDirection * stride) + lateral;
            Vector3 surfacePoint = SampleGround(candidate, up);
            leg.desiredPosition = surfacePoint
                + gravitySource.GetUp(surfacePoint) * leg.footSoleOffset;

            float phase = Mathf.Repeat(cycle, 1f);
            bool swinging = CurrentAction == CreatureActionSemantic.Locomotion && phase < swingDuration;
            if (!leg.initialized)
            {
                leg.plantedPosition = leg.desiredPosition;
                leg.swingStart = leg.desiredPosition;
                leg.swingTarget = leg.desiredPosition;
                leg.initialized = true;
            }
            if (swinging && !leg.wasSwinging)
            {
                leg.swingStart = leg.plantedPosition;
                leg.swingTarget = leg.desiredPosition;
            }

            Vector3 footGoal;
            if (swinging)
            {
                float t = Mathf.Clamp01(phase / swingDuration);
                Vector3 arcUp = gravitySource.GetUp(Vector3.Lerp(leg.swingStart, leg.swingTarget, t));
                footGoal = Vector3.Lerp(leg.swingStart, leg.swingTarget, t)
                    + arcUp * (Mathf.Sin(t * Mathf.PI) * genome.gaitHeight);
                if (t >= 0.99f)
                    leg.plantedPosition = leg.swingTarget;
            }
            else
            {
                footGoal = leg.plantedPosition;
                float plantedReach = Vector3.Distance(hip, footGoal);
                bool plantedFootCannotReach = plantedReach > leg.TotalLength * 0.98f;
                bool idleFootDrifted = CurrentAction == CreatureActionSemantic.Idle
                    && Vector3.Distance(footGoal, leg.desiredPosition) > leg.TotalLength * 0.22f;
                if (plantedFootCannotReach || idleFootDrifted)
                {
                    leg.plantedPosition = leg.desiredPosition;
                    footGoal = leg.plantedPosition;
                }
            }

            leg.wasSwinging = swinging;
            SolveTwoBoneGoal(leg, hip, footGoal, forward, up);
        }
    }

    Vector3 SampleGround(Vector3 candidate, Vector3 up)
    {
        Vector3 origin = candidate + up * 2.5f;
        Vector3 down = -gravitySource.GetUp(origin);
        int hitCount = Physics.RaycastNonAlloc(origin, down, groundHits, 10f, groundLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        Vector3 result = gravitySource.GetSurfacePoint(candidate - gravitySource.Center);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider == null || hit.collider.attachedRigidbody == body || hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            result = hit.point;
        }
        return result;
    }

    void SolveTwoBoneGoal(CreatureLegRig leg, Vector3 hip, Vector3 requestedFoot, Vector3 forward, Vector3 up)
    {
        Vector3 toFoot = requestedFoot - hip;
        float requestedDistance = Mathf.Max(0.001f, toFoot.magnitude);
        Vector3 direction = toFoot / requestedDistance;
        float minimum = Mathf.Abs(leg.upperLength - leg.lowerLength) + 0.001f;
        float maximum = Mathf.Max(minimum, leg.TotalLength - 0.001f);
        float distance = Mathf.Clamp(requestedDistance, minimum, maximum);
        Vector3 footGoal = hip + direction * distance;

        float along = (leg.upperLength * leg.upperLength
            - leg.lowerLength * leg.lowerLength
            + distance * distance) / (2f * distance);
        float bendHeight = Mathf.Sqrt(Mathf.Max(0f, leg.upperLength * leg.upperLength - along * along));
        Vector3 bendHint = Vector3.ProjectOnPlane(forward * (leg.IsFront ? 1f : -1f), direction);
        if (bendHint.sqrMagnitude < 0.0001f)
            bendHint = Vector3.ProjectOnPlane(up, direction);
        bendHint.Normalize();
        Vector3 knee = hip + direction * along + bendHint * bendHeight;

        SetBoneDownAxis(leg.upper, knee - hip, bendHint);
        Vector3 actualKnee = leg.lower.position;
        SetBoneDownAxis(leg.lower, footGoal - actualKnee, bendHint);

        Vector3 footUp = gravitySource.GetUp(footGoal);
        Vector3 footForward = Vector3.ProjectOnPlane(forward, footUp);
        if (footForward.sqrMagnitude < 0.0001f)
            footForward = Vector3.ProjectOnPlane(transform.forward, footUp);
        leg.foot.rotation = Quaternion.LookRotation(footForward.normalized, footUp);
    }

    static void SetBoneDownAxis(Transform bone, Vector3 downDirection, Vector3 bendReference)
    {
        if (downDirection.sqrMagnitude < 0.0001f)
            return;
        Vector3 down = downDirection.normalized;
        Vector3 reference = Vector3.ProjectOnPlane(bendReference, down);
        if (reference.sqrMagnitude < 0.0001f)
            reference = Vector3.ProjectOnPlane(Vector3.forward, down);
        bone.rotation = Quaternion.LookRotation(reference.normalized, -down);
    }

    void OnDrawGizmosSelected()
    {
        if (rig == null || rig.legs == null)
            return;
        foreach (CreatureLegRig leg in rig.legs)
        {
            if (!leg.initialized)
                continue;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(leg.desiredPosition, 0.1f);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(leg.plantedPosition, 0.12f);
        }
    }
}
