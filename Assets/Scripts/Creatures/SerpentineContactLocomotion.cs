using UnityEngine;

[DisallowMultipleComponent]
public sealed class SerpentineContactLocomotion : MonoBehaviour
{
    const float MaximumSurfaceSpeed = 7.5f;
    readonly RaycastHit[] groundHits = new RaycastHit[16];

    SphericalGravitySource gravitySource;
    Rigidbody body;
    CreatureRig rig;
    CreatureGenome genome;
    LayerMask groundLayers;
    Quaternion[] spineRestRotations;
    Quaternion tailRestRotation;
    Vector3[] previousSamplePositions;
    Vector3[] samplePositions;
    Vector3[] contactPoints;
    Vector3[] contactNormals;
    bool[] contacts;
    bool samplesInitialized;

    public int ContactCount { get; private set; }
    public Vector3 TotalPropulsionForce { get; private set; }
    public float ForwardPropulsionForce { get; private set; }
    public float WavePhase { get; private set; }
    public bool LocomotionEnabled { get; private set; } = true;

    public void Configure(
        SphericalGravitySource source,
        Rigidbody targetBody,
        CreatureRig creatureRig,
        CreatureGenome creatureGenome,
        LayerMask layers)
    {
gravitySource = source;
        body = targetBody;
        rig = creatureRig;
        genome = creatureGenome;
        groundLayers = layers;
        int count = rig.spineBones.Length;
        spineRestRotations = new Quaternion[count];
        previousSamplePositions = new Vector3[count];
        samplePositions = new Vector3[count];
        contactPoints = new Vector3[count];
        contactNormals = new Vector3[count];
        contacts = new bool[count];
        for (int i = 0; i < count; i++)
            spineRestRotations[i] = rig.spineBones[i].localRotation;
        tailRestRotation = rig.tailBase.localRotation;
    
}

    public void SetLocomotionEnabled(bool enabled)
    {
LocomotionEnabled = enabled;
    
}

    void FixedUpdate()
    {
        if (gravitySource == null || body == null || rig == null || genome == null)
            return;

        float deltaTime = Mathf.Max(Time.fixedDeltaTime, 0.001f);
        if (LocomotionEnabled)
            WavePhase = Mathf.Repeat(WavePhase + genome.gaitFrequency * Mathf.PI * 2f * deltaTime, Mathf.PI * 2f);
        ApplySpineWave();
        SampleGroundContacts();
        ApplyContactForces(deltaTime);
    }

    void ApplySpineWave()
    {
        float amplitude = LocomotionEnabled ? genome.serpentineWaveAmplitude : 0f;
        int last = rig.spineBones.Length - 1;
        for (int i = 0; i < rig.spineBones.Length; i++)
        {
            int distanceFromHead = last - i;
            float yaw = Mathf.Sin(WavePhase - distanceFromHead * genome.serpentinePhaseLag)
                * amplitude * 0.36f;
            float pitch = Mathf.Sin(WavePhase * 0.5f - distanceFromHead * 0.45f)
                * amplitude * 0.025f;
            rig.spineBones[i].localRotation = spineRestRotations[i] * Quaternion.Euler(pitch, yaw, 0f);
        }

        float tailYaw = Mathf.Sin(WavePhase - rig.spineBones.Length * genome.serpentinePhaseLag)
            * amplitude * 0.48f;
        rig.tailBase.localRotation = tailRestRotation * Quaternion.Euler(0f, tailYaw, 0f);
    }

    void SampleGroundContacts()
    {
        ContactCount = 0;
        float castLift = Mathf.Max(0.35f, genome.bodyHeight * 0.75f);
        float castDistance = castLift + Mathf.Max(0.65f, genome.bodyHeight * 1.3f);
        for (int i = 0; i < rig.spineBones.Length; i++)
        {
            Vector3 sample = rig.spineBones[i].position;
            samplePositions[i] = sample;
            Vector3 up = gravitySource.GetUp(sample);
            int hitCount = Physics.RaycastNonAlloc(
                sample + up * castLift,
                -up,
                groundHits,
                castDistance,
                groundLayers,
                QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            contacts[i] = false;
            for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                RaycastHit hit = groundHits[hitIndex];
                if (hit.collider == null || hit.collider.attachedRigidbody == body || hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                contactPoints[i] = hit.point;
                contactNormals[i] = hit.normal;
                contacts[i] = true;
            }
            if (contacts[i])
                ContactCount++;
        }

        if (!samplesInitialized)
        {
            for (int i = 0; i < samplePositions.Length; i++)
                previousSamplePositions[i] = samplePositions[i];
            samplesInitialized = true;
        }
    }

    void ApplyContactForces(float deltaTime)
    {
        TotalPropulsionForce = Vector3.zero;
        ForwardPropulsionForce = 0f;
        if (!samplesInitialized || ContactCount < 2 || !LocomotionEnabled)
        {
            StoreSamplePositions();
            return;
        }

        float gravityMagnitude = gravitySource.GetGravity(body.position).magnitude;
        float normalLoadPerContact = body.mass * gravityMagnitude / ContactCount;
        float maximumForcePerContact = normalLoadPerContact * 4f;
        Vector3 surfaceUp = gravitySource.GetUp(body.position);
        Vector3 intendedForward = Vector3.ProjectOnPlane(transform.forward, surfaceUp).normalized;
        float currentForwardSpeed = Vector3.Dot(body.velocity, intendedForward);
        float tractionSpeedFactor = Mathf.Clamp01(1f - Mathf.Max(0f, currentForwardSpeed) / MaximumSurfaceSpeed);
        for (int i = 0; i < rig.spineBones.Length; i++)
        {
            if (!contacts[i])
                continue;
            Vector3 normal = contactNormals[i].normalized;
            Vector3 segmentForward = Vector3.ProjectOnPlane(rig.spineBones[i].forward, normal);
            if (segmentForward.sqrMagnitude < 0.0001f)
                continue;
            segmentForward.Normalize();
            Vector3 lateral = Vector3.Cross(normal, segmentForward).normalized;
            Vector3 animatedPointVelocity = (samplePositions[i] - previousSamplePositions[i]) / deltaTime;
            float lateralSpeed = Vector3.Dot(animatedPointVelocity, lateral);
            float longitudinalSpeed = Vector3.Dot(animatedPointVelocity, segmentForward);
            float longitudinalFriction = longitudinalSpeed < 0f
                ? genome.serpentineBackwardFriction
                : genome.serpentineLongitudinalFriction;
            Vector3 reaction = -lateral * (lateralSpeed * genome.serpentineLateralFriction)
                - segmentForward * (longitudinalSpeed * longitudinalFriction);
            reaction *= body.mass / rig.spineBones.Length;
            Vector3 scaleTraction = intendedForward
                * (Mathf.Abs(lateralSpeed) * genome.serpentineTractionEfficiency
                    * body.mass / rig.spineBones.Length * tractionSpeedFactor);
            reaction += scaleTraction;
            reaction = Vector3.ClampMagnitude(reaction, maximumForcePerContact);
            body.AddForceAtPosition(reaction, contactPoints[i], ForceMode.Force);
            TotalPropulsionForce += reaction;
            ForwardPropulsionForce += Vector3.Dot(reaction, intendedForward);
        }

        Vector3 radialUp = gravitySource.GetUp(body.position);
        Vector3 tangentialVelocity = Vector3.ProjectOnPlane(body.velocity, radialUp);
        if (tangentialVelocity.magnitude > MaximumSurfaceSpeed)
        {
            Vector3 excess = tangentialVelocity.normalized * (tangentialVelocity.magnitude - MaximumSurfaceSpeed);
            body.AddForce(-excess * body.mass * 4f, ForceMode.Force);
        }
        StoreSamplePositions();
    }

    void StoreSamplePositions()
    {
        for (int i = 0; i < samplePositions.Length; i++)
            previousSamplePositions[i] = samplePositions[i];
    }

    void OnDrawGizmosSelected()
    {
        if (contacts == null)
            return;
        for (int i = 0; i < contacts.Length; i++)
        {
            if (!contacts[i])
                continue;
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(contactPoints[i], 0.08f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(contactPoints[i], contactPoints[i] + contactNormals[i] * 0.35f);
        }
        if (body != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(body.position, body.position + TotalPropulsionForce * 0.02f);
        }
    }
}
