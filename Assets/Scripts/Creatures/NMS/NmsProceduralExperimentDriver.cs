using UnityEngine;

[DisallowMultipleComponent]
public sealed class NmsProceduralExperimentDriver : MonoBehaviour
{
    [SerializeField] NmsRandomCreatureGenerator generator;
    [SerializeField] SphericalGravitySource gravitySource;
    [SerializeField] bool autoWalk;
    [SerializeField] bool showTelemetry = true;

    NmsProceduralMotionController controller;
    NmsImportedAnimatorMotionController importedController;
    GUIStyle telemetryStyle;

    void Awake()
    {
        if (generator == null) generator = GetComponent<NmsRandomCreatureGenerator>();
        if (gravitySource == null) gravitySource = GetComponentInParent<SphericalGravitySource>();
        if (generator != null) generator.CreatureReady += HandleCreatureReady;
    }

    void Start()
    {
        if (generator != null && generator.CurrentMotionController != null)
            controller = generator.CurrentMotionController;
        if (generator != null && generator.CurrentImportedController != null)
            importedController = generator.CurrentImportedController;
    }

    void OnDestroy()
    {
        if (generator != null) generator.CreatureReady -= HandleCreatureReady;
    }

    void HandleCreatureReady(NmsCreatureSpawnContext context)
    {
        controller = context.proceduralController;
        importedController = context.importedController;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T)) autoWalk = !autoWalk;
        if (generator != null && Input.GetKeyDown(KeyCode.R))
        {
            controller = null;
            importedController = null;
            generator.Generate(unchecked(generator.Seed + 1));
            return;
        }
        if ((controller == null && importedController == null) || gravitySource == null)
            return;

        Transform target = controller != null
            ? controller.transform : importedController.transform;
        Vector3 currentForward = controller != null
            ? controller.SurfaceForward : importedController.SurfaceForward;
        Vector3 up = gravitySource.GetUp(target.position);
        Camera view = Camera.main;
        Vector3 forward = autoWalk
            ? currentForward
            : view != null
                ? Vector3.ProjectOnPlane(view.transform.forward, up)
                : currentForward;
        if (forward.sqrMagnitude < 0.0001f) forward = currentForward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(up, forward).normalized;
        Vector2 input = autoWalk
            ? Vector2.up
            : new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        Vector3 direction = Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
        float walkSpeed = controller != null
            ? controller.WalkSpeed : importedController.WalkSpeed;
        float runSpeed = controller != null
            ? controller.RunSpeed : importedController.RunSpeed;
        float speed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;
        Vector3 facing = direction.sqrMagnitude > 0.0001f
            ? direction
            : currentForward;
        var motionCommand = new CreatureMotionCommand
        {
            desiredVelocityWorld = direction * speed,
            desiredFacingWorld = facing,
            jumpRequested = controller != null && Input.GetKeyDown(KeyCode.Space),
            attackRequested = controller != null && Input.GetKeyDown(KeyCode.F),
            attackTargetWorld = target.position + facing * 4f
        };
        if (controller != null)
            controller.SetCommand(motionCommand);
        else
            importedController.SetCommand(motionCommand);
    }

    void OnGUI()
    {
        if (!showTelemetry || generator == null) return;
        if (telemetryStyle == null)
        {
            telemetryStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white }
            };
        }
        GUI.Box(new Rect(12f, 12f, 500f, 330f), GUIContent.none);
        GUILayout.BeginArea(new Rect(24f, 20f, 476f, 316f));
        GUILayout.Label("NMS PROCEDURAL MOTION", telemetryStyle);
        GUILayout.Label($"Seed: {generator.Seed}", telemetryStyle);
        GUILayout.Label($"Family: {generator.CurrentFamily?.FamilyId ?? "-"}", telemetryStyle);
        GUILayout.Label(
            $"Motion: {generator.MotionMode}   Validated: {generator.CurrentVariantValidated}",
            telemetryStyle);
        GUILayout.Label($"Signature: {ShortSignature(generator.CurrentSpecies?.signature)}", telemetryStyle);
        if (controller != null)
        {
            GUILayout.Label(
                $"State: {controller.CurrentState}   Speed: {controller.CurrentSpeed:F2}",
                telemetryStyle);
            GUILayout.Label(
                $"Feet: {controller.ContactFootCount}/4   Stable: {controller.IsStable}",
                telemetryStyle);
            GUILayout.Label(
                $"Physics: {controller.PhysicsMode}   Phase: {controller.GaitPhase:F2}",
                telemetryStyle);
            GUILayout.Label(
                $"Height error: {controller.BodyHeightError:F3}   "
                + $"Normal speed: {controller.NormalVelocity:F3}", telemetryStyle);
            GUILayout.Label(
                $"Legs: {controller.LegStateSummary}   "
                + $"Slip: {controller.MaxStanceFootError:F3}", telemetryStyle);
            GUILayout.Label(
                $"Bone error: {controller.MaxSegmentLengthError:F5}   "
                + $"Air: {controller.LastAirborneReason}", telemetryStyle);
            GUILayout.Label(
                $"Trajectory: {controller.TrajectoryMode}   "
                + $"Stride: {controller.TemplateStrideScale:F2}   "
                + $"Residual: {controller.MaxTemplateTrajectoryResidual:F3}",
                telemetryStyle);
            GUILayout.Label(
                $"Pole weight: {controller.PoleTemplateWeight:F2}   "
                + $"Knee/Hock delta: {controller.MaxKneePoleDelta:F1}/"
                + $"{controller.MaxHockPoleDelta:F1}", telemetryStyle);
            GUILayout.Label(
                $"IK reachable: {controller.AllLegsIkReachable}   "
                + $"Pole fallback: {controller.PoleFallbackReason}", telemetryStyle);
            GUILayout.Label(
                $"Animator disabled: {controller.AnimatorDisabled}   Auto: {autoWalk}",
                telemetryStyle);
        }
        else if (importedController != null)
        {
            GUILayout.Label(
                $"State: {importedController.MotionState}   "
                + $"Speed: {importedController.CurrentSpeed:F2}", telemetryStyle);
            GUILayout.Label(
                $"Walk weight: {importedController.AnimationBlend:F2}   "
                + $"Animator ready: {importedController.AnimatorReady}", telemetryStyle);
            GUILayout.Label(
                $"Skin validation: {generator.LastValidationResult}", telemetryStyle);
            GUILayout.Label(
                $"Controls: WASD / R   Auto: {autoWalk}", telemetryStyle);
        }
        GUILayout.EndArea();
    }

    static string ShortSignature(string signature)
    {
        if (string.IsNullOrEmpty(signature)) return "-";
        return signature.Length <= 38 ? signature : signature.Substring(0, 38) + "...";
    }
}
