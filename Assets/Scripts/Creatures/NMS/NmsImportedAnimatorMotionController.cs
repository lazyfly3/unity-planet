using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NmsImportedAnimatorMotionController : MonoBehaviour
{
    const float CommandTimeout = 0.3f;

    [SerializeField, Range(0.5f, 3f)] float walkPlaybackSpeed = 1.8f;

    SphericalGravitySource gravitySource;
    NmsCreatureFamilyDefinition family;
    Animator animator;
    LayerMask groundLayers;
    CreatureMotionCommand command;
    Vector3 localForwardAxis = Vector3.forward;
    Vector3 surfaceForward = Vector3.forward;
    string locomotionState = "Locomotion";
    string speedParameter = "Speed";
    int locomotionStateHash;
    int speedParameterHash;
    float surfaceProbeHeight = 20f;
    float commandAge;
    bool hasCommand;
    bool animationWasMoving;

    public float CurrentSpeed { get; private set; }
    public float WalkSpeed => family != null ? family.WalkSpeed : 0f;
    public float RunSpeed => WalkSpeed;
    public float AnimationBlend { get; private set; }
    public bool AnimatorReady { get; private set; }
    public Vector3 SurfaceForward => surfaceForward;
    public string MotionState => AnimationBlend < 0.08f ? "Stopped" : "Walk";

    public bool Configure(
        SphericalGravitySource source,
        NmsCreatureFamilyDefinition sourceFamily,
        Animator sourceAnimator,
        RuntimeAnimatorController controller,
        string stateName,
        string parameterName,
        LayerMask layers,
        float probeHeight,
        float playbackSpeed,
        out string error)
    {
        gravitySource = source;
        family = sourceFamily;
        animator = sourceAnimator;
        groundLayers = layers;
        surfaceProbeHeight = Mathf.Max(1f, probeHeight);
        walkPlaybackSpeed = Mathf.Clamp(playbackSpeed, 0.5f, 3f);
        locomotionState = string.IsNullOrEmpty(stateName) ? "Locomotion" : stateName;
        speedParameter = string.IsNullOrEmpty(parameterName) ? "Speed" : parameterName;
        error = null;
        if (gravitySource == null || family == null || animator == null || controller == null)
        {
            error = "Imported motion requires gravity, family, Animator and controller.";
            return false;
        }

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        animator.Update(0f);
        speedParameterHash = Animator.StringToHash(speedParameter);
        bool hasSpeed = false;
        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
            if (parameters[i].nameHash == speedParameterHash
                && parameters[i].type == AnimatorControllerParameterType.Float)
            {
                hasSpeed = true;
                break;
            }
        locomotionStateHash = Animator.StringToHash("Base Layer." + locomotionState);
        int shortStateHash = Animator.StringToHash(locomotionState);
        bool hasLocomotion = animator.HasState(0, locomotionStateHash)
            || animator.HasState(0, shortStateHash);
        if (!hasSpeed || !hasLocomotion)
        {
            error = $"Animator is missing state '{locomotionState}' or float '{speedParameter}'.";
            return false;
        }

        localForwardAxis = family.LocalForwardAxis.sqrMagnitude > 0.000001f
            ? family.LocalForwardAxis.normalized : Vector3.forward;
        Vector3 up = gravitySource.GetUp(transform.position);
        surfaceForward = Vector3.ProjectOnPlane(
            transform.TransformDirection(localForwardAxis), up).normalized;
        if (surfaceForward.sqrMagnitude < 0.000001f)
            surfaceForward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        animator.SetFloat(speedParameterHash, 0.5f);
        animator.Play(locomotionStateHash, 0, 0f);
        animator.Update(0f);
        animator.speed = 0f;
        AnimatorReady = true;
        return true;
    }

    public void SetCommand(CreatureMotionCommand value)
    {
        command = value;
        commandAge = 0f;
        hasCommand = true;
    }

    void FixedUpdate()
    {
        if (!AnimatorReady || gravitySource == null || family == null)
            return;
        float dt = Mathf.Max(0.001f, Time.fixedDeltaTime);
        commandAge += dt;
        if (commandAge > CommandTimeout)
        {
            hasCommand = false;
            command = default;
        }

        Vector3 up = gravitySource.GetUp(transform.position);
        Vector3 requestedVelocity = hasCommand
            ? Vector3.ProjectOnPlane(command.desiredVelocityWorld, up)
            : Vector3.zero;
        float targetSpeed = Mathf.Min(requestedVelocity.magnitude, RunSpeed);
        float rate = targetSpeed > CurrentSpeed ? RunSpeed * 3.5f : RunSpeed * 4.5f;
        CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetSpeed, rate * dt);

        Vector3 requestedFacing = hasCommand
            ? Vector3.ProjectOnPlane(command.desiredFacingWorld, up)
            : Vector3.zero;
        if (requestedFacing.sqrMagnitude < 0.0001f)
            requestedFacing = requestedVelocity;
        if (requestedFacing.sqrMagnitude > 0.0001f)
            surfaceForward = Vector3.RotateTowards(
                surfaceForward, requestedFacing.normalized,
                240f * Mathf.Deg2Rad * dt, 0f).normalized;

        Quaternion targetRotation = RotationForLocalForward(
            localForwardAxis, surfaceForward, up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, targetRotation, 240f * dt);
        if (CurrentSpeed > 0.001f)
            MoveAlongSphere(CurrentSpeed * dt);

        bool moving = CurrentSpeed > 0.01f;
        if (!moving && animationWasMoving)
        {
            animator.Play(locomotionStateHash, 0, 0f);
            animator.Update(0f);
        }
        animationWasMoving = moving;
        AnimationBlend = moving ? 0.5f : 0f;
        animator.SetFloat(speedParameterHash, 0.5f);
        animator.speed = moving
            ? walkPlaybackSpeed * Mathf.Clamp(
                CurrentSpeed / Mathf.Max(0.01f, WalkSpeed), 0.25f, 1f)
            : 0f;
    }

    void MoveAlongSphere(float distance)
    {
        Vector3 center = gravitySource.Center;
        Vector3 currentUp = gravitySource.GetUp(transform.position);
        Vector3 tangentForward = Vector3.ProjectOnPlane(surfaceForward, currentUp);
        if (tangentForward.sqrMagnitude < 0.000001f)
            return;
        tangentForward.Normalize();
        Vector3 orbitAxis = Vector3.Cross(currentUp, tangentForward).normalized;
        float radius = Mathf.Max(
            gravitySource.Radius, Vector3.Distance(transform.position, center));
        Vector3 nextUp = Quaternion.AngleAxis(
            distance / radius * Mathf.Rad2Deg, orbitAxis) * currentUp;
        nextUp.Normalize();
        Vector3 surface = FindSurface(nextUp);
        transform.position = surface + nextUp * family.SurfaceClearance;
        surfaceForward = Vector3.ProjectOnPlane(
            Quaternion.FromToRotation(currentUp, nextUp) * tangentForward,
            nextUp).normalized;
        transform.rotation = RotationForLocalForward(
            localForwardAxis, surfaceForward, nextUp);
    }

    Vector3 FindSurface(Vector3 radialUp)
    {
        Vector3 origin = gravitySource.Center
            + radialUp * (gravitySource.Radius + surfaceProbeHeight);
        if (Physics.Raycast(
            origin, -radialUp, out RaycastHit hit,
            surfaceProbeHeight * 2f, groundLayers,
            QueryTriggerInteraction.Ignore))
            return hit.point;
        return gravitySource.GetSurfacePoint(radialUp);
    }

    static Quaternion RotationForLocalForward(
        Vector3 localForward, Vector3 worldForward, Vector3 worldUp)
    {
        Vector3 axis = localForward.sqrMagnitude > 0.000001f
            ? localForward.normalized : Vector3.forward;
        return Quaternion.LookRotation(worldForward, worldUp)
            * Quaternion.Inverse(Quaternion.LookRotation(axis, Vector3.up));
    }
}
