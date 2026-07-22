using System.Collections.Generic;
using SpacecraftEditor;
using UnityEngine;

[DefaultExecutionOrder(-560)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(PirateCommandBuffer))]
public sealed class PirateShipAiController : MonoBehaviour
{
    [Header("Flight")]
    [SerializeField, Min(1f)] float preferredDistance = 34f;
    [SerializeField, Min(1f)] float retreatDistance = 10f;
    [SerializeField, Min(1f)] float approachDistance = 48f;
    [SerializeField, Range(0f, 1f)] float orbitThrust = 0.42f;
    [SerializeField, Range(10f, 90f)] float fullSteeringAngle = 48f;
    [SerializeField, Range(0f, 1f)] float avoidanceThrust = 0.85f;
    [SerializeField, Min(5f)] float obstacleLookAhead = 90f;

    [Header("Combat")]
    [SerializeField, Min(1f)] float fireAcquireDistance = 48f;
    [SerializeField, Min(1f)] float fireReleaseDistance = 50f;
    [SerializeField, Range(1f, 30f)] float fireAlignmentDegrees = 9f;
    [SerializeField, Range(0f, 1f)] float outgoingDamageMultiplier = 0.05f;

    readonly RaycastHit[] obstacleHits = new RaycastHit[8];
    Rigidbody body;
    PirateCommandBuffer commands;
    SpacecraftIfcsMotor ifcs;
    SpacecraftWeaponSystem weapons;
    SpaceCombatant selfCombatant;
    SpaceCombatant target;
    float orbitDirection = 1f;
    int tier = 1;
    bool fireRangeLatched;

    public SpaceCombatant Target => target;
    public bool IsActivelyAttacking { get; private set; }
    public float LastAttackTime { get; private set; } = float.NegativeInfinity;
    public Vector3 AimPosition => selfCombatant == null ? transform.position : selfCombatant.AimPosition;
    public float DistanceToTarget { get; private set; } = float.PositiveInfinity;

    void Awake()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();
        EnableSystems(true);
    }

    public void Configure(SpaceCombatant targetCombatant, int seed, int difficultyTier)
    {
        ResolveReferences();
        target = targetCombatant;
        tier = Mathf.Clamp(difficultyTier, 1, 5);
        orbitDirection = (seed & 1) == 0 ? 1f : -1f;
        preferredDistance = Mathf.Clamp(30f + tier * 2f, 25f, 40f);
        retreatDistance = 10f;
        approachDistance = 48f;
        fireAcquireDistance = 48f;
        fireReleaseDistance = 50f;
        fireRangeLatched = false;
        IsActivelyAttacking = false;
        weapons?.SetDamageMultiplier(outgoingDamageMultiplier);
        EnableSystems(true);
    }

    void Update()
    {
        if (!TryResolveTarget())
        {
            commands?.Clear();
            fireRangeLatched = false;
            IsActivelyAttacking = false;
            DistanceToTarget = float.PositiveInfinity;
            return;
        }

        Vector3 origin = body == null ? transform.position : body.worldCenterOfMass;
        Vector3 targetPosition = target.AimPosition;
        Vector3 targetVelocity = target.Velocity;
        float distance = Vector3.Distance(origin, targetPosition);
        DistanceToTarget = distance;
        float leadTime = Mathf.Clamp(distance / 650f, 0f, 1.35f);
        Vector3 aimPoint = targetPosition + targetVelocity * leadTime;
        Vector3 worldDirection = aimPoint - origin;
        if (worldDirection.sqrMagnitude < 0.001f)
            worldDirection = transform.forward;

        Vector3 localDirection = transform.InverseTransformDirection(worldDirection.normalized);
        float yawDegrees = Mathf.Atan2(localDirection.x, Mathf.Max(0.001f, localDirection.z)) * Mathf.Rad2Deg;
        float pitchDegrees = Mathf.Atan2(
            localDirection.y,
            Mathf.Max(0.001f, new Vector2(localDirection.x, localDirection.z).magnitude)) * Mathf.Rad2Deg;
        Vector2 steering = new Vector2(
            Mathf.Clamp(-pitchDegrees / fullSteeringAngle, -1f, 1f),
            Mathf.Clamp(yawDegrees / fullSteeringAngle, -1f, 1f));

        Vector3 translation = CalculateTranslation(distance);
        ApplyObstacleAvoidance(ref translation);
        commands.SetFlightCommand(new SpacecraftFlightCommand
        {
            translation = Vector3.ClampMagnitude(translation, 1f),
            vjoy = steering,
            roll = Mathf.Clamp(-steering.y * 0.28f, -0.45f, 0.45f),
            boost = distance > approachDistance * 1.8f,
            brake = false
        });

        float alignment = Vector3.Angle(transform.forward, worldDirection);
        if (distance > fireReleaseDistance)
            fireRangeLatched = false;
        else if (distance <= fireAcquireDistance)
            fireRangeLatched = true;
        bool fire = fireRangeLatched && alignment <= fireAlignmentDegrees;
        IsActivelyAttacking = fire;
        if (fire)
            LastAttackTime = Time.time;
        commands.SetFireCommand(new SpacecraftFireCommand(1, fire, true));
    }

    Vector3 CalculateTranslation(float distance)
    {
        float forward;
        if (distance > approachDistance)
            forward = 1f;
        else if (distance < retreatDistance)
            forward = -0.75f;
        else
            forward = Mathf.Clamp((distance - preferredDistance) / Mathf.Max(1f, approachDistance - preferredDistance), -0.2f, 0.35f);
        float orbit = distance < approachDistance * 1.2f ? orbitDirection * orbitThrust : 0f;
        return new Vector3(orbit, 0f, forward);
    }

    void ApplyObstacleAvoidance(ref Vector3 translation)
    {
        Vector3 origin = body == null ? transform.position : body.worldCenterOfMass;
        int count = Physics.SphereCastNonAlloc(
            origin,
            3f,
            transform.forward,
            obstacleHits,
            obstacleLookAhead,
            ~0,
            QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        Vector3 avoidance = Vector3.zero;
        for (int index = 0; index < count; index++)
        {
            Collider collider = obstacleHits[index].collider;
            if (collider == null || collider.transform.IsChildOf(transform) ||
                (target != null && target.Owns(collider.transform)) || obstacleHits[index].distance >= nearest)
                continue;
            nearest = obstacleHits[index].distance;
            Vector3 localNormal = transform.InverseTransformDirection(obstacleHits[index].normal);
            avoidance = new Vector3(
                Mathf.Abs(localNormal.x) < 0.1f ? orbitDirection : Mathf.Sign(localNormal.x),
                Mathf.Sign(localNormal.y),
                -0.25f);
        }
        if (nearest < float.PositiveInfinity)
            translation = Vector3.Lerp(translation, avoidance.normalized, avoidanceThrust);
    }

    bool TryResolveTarget()
    {
        if (target != null && target.IsTargetable)
            return true;
        IReadOnlyList<SpaceCombatant> combatants = SpaceCombatant.Active;
        float bestDistance = float.PositiveInfinity;
        SpaceCombatant best = null;
        for (int index = 0; index < combatants.Count; index++)
        {
            SpaceCombatant candidate = combatants[index];
            if (candidate == null || !candidate.IsTargetable || selfCombatant == null || !selfCombatant.IsHostileTo(candidate))
                continue;
            float distance = (candidate.AimPosition - transform.position).sqrMagnitude;
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            best = candidate;
        }
        target = best;
        return target != null;
    }

    void ResolveReferences()
    {
        if (body == null)
            body = GetComponent<Rigidbody>();
        if (commands == null)
            commands = GetComponent<PirateCommandBuffer>();
        if (ifcs == null)
            ifcs = GetComponent<SpacecraftIfcsMotor>();
        if (weapons == null)
            weapons = GetComponent<SpacecraftWeaponSystem>();
        if (selfCombatant == null)
            selfCombatant = GetComponent<SpaceCombatant>();
    }

    void EnableSystems(bool value)
    {
        if (ifcs != null)
            ifcs.ControlsEnabled = value;
        if (weapons != null)
            weapons.ControlsEnabled = value;
    }

    void OnDisable()
    {
        commands?.Clear();
        fireRangeLatched = false;
        IsActivelyAttacking = false;
        DistanceToTarget = float.PositiveInfinity;
        EnableSystems(false);
    }
}
