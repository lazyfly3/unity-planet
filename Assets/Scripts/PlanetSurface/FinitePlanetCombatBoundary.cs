using UnityEngine;

[DefaultExecutionOrder(9000)]
[DisallowMultipleComponent]
public sealed class FinitePlanetCombatBoundary : MonoBehaviour
{
    const float SafePoseRadiusFraction = 0.72f;
    const float WarningRadiusFraction = 0.86f;
    const float LowerRecoveryDepth = 80f;
    const float ResetNoticeDuration = 2.5f;
    const float SoftCeilingBand = 96f;
    const float MaximumCeilingAcceleration = 72f;

    InfinitePlanarSurfaceWorld world;
    PlanarSurfaceModularVehicleLoader vehicleLoader;
    Rigidbody shipBody;
    Vector3 battleCenter;
    Vector3 safePosition;
    Quaternion safeRotation;
    float warningRadius;
    float hardRadius;
    float flightCeiling;
    float lastResetTime = float.NegativeInfinity;
    bool initialized;
    bool hasSafePose;
    bool warningActive;

    public Vector3 BattleCenter => battleCenter;
    public float WarningRadius => warningRadius;
    public float HardRadius => hardRadius;
    public float FlightCeiling => flightCeiling;
    public bool IsInitialized => initialized;
    public bool IsWarningActive => warningActive;

    public void Configure(InfinitePlanarSurfaceWorld targetWorld)
    {
        world = targetWorld;
        hardRadius = world != null
            ? world.FiniteCombatRadius
            : InfinitePlanarSurfaceWorld.DefaultFiniteCombatRadius;
        warningRadius = hardRadius * WarningRadiusFraction;
        initialized = false;
        hasSafePose = false;
        warningActive = false;
    }

    void Update()
    {
        EnsureInitialized();
        ResolveShipBody();
    }

    void FixedUpdate()
    {
        if (!EnsureInitialized() || !ResolveShipBody())
            return;
        if (shipBody.isKinematic)
            return;

        Vector3 position = shipBody.position;
        warningActive = IsOutsideWarningBounds(
            position,
            battleCenter,
            warningRadius,
            flightCeiling);
        ApplySoftCeiling(position.y);

        // Normal horizontal or vertical boundary approaches are never
        // teleported. The collision-backed mountain wall and the soft ceiling
        // handle them. Recovery remains only for a genuine fall below the
        // generated terrain window.
        if (RequiresEmergencyRecovery(
                position,
                battleCenter,
                LowerRecoveryDepth))
        {
            RecoverShip();
            return;
        }

        float safeRadius = warningRadius * SafePoseRadiusFraction;
        Vector2 planarDelta = new Vector2(
            position.x - battleCenter.x,
            position.z - battleCenter.z);
        if (planarDelta.sqrMagnitude <= safeRadius * safeRadius
            && position.y <= flightCeiling - 20f
            && position.y >= battleCenter.y - 10f)
        {
            safePosition = position;
            safeRotation = shipBody.rotation;
            hasSafePose = true;
        }
    }

    bool EnsureInitialized()
    {
        if (initialized)
            return true;
        if (world == null || !world.IsCenterCollisionReady)
            return false;
        if (!world.TryProjectToSurface(
                world.transform.position,
                out PlanetSurfaceSample sample))
        {
            return false;
        }

        battleCenter = sample.point;
        hardRadius = world.FiniteCombatRadius;
        warningRadius = hardRadius * WarningRadiusFraction;
        flightCeiling = battleCenter.y
            + world.FiniteCombatFlightCeilingHeight;
        initialized = true;
        return true;
    }

    bool ResolveShipBody()
    {
        if (shipBody != null)
            return true;
        if (vehicleLoader == null)
        {
            vehicleLoader = GetComponent<
                PlanarSurfaceModularVehicleLoader>();
        }
        if (vehicleLoader == null
            || !vehicleLoader.IsBuilt
            || vehicleLoader.Body == null)
        {
            return false;
        }

        shipBody = vehicleLoader.Body;
        if (RequiresEmergencyRecovery(
                shipBody.position,
                battleCenter,
                LowerRecoveryDepth))
        {
            safePosition = battleCenter + Vector3.up * 20f;
            Vector3 forward = Vector3.ProjectOnPlane(
                shipBody.rotation * Vector3.forward,
                Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            safeRotation = Quaternion.LookRotation(
                forward.normalized,
                Vector3.up);
        }
        else
        {
            safePosition = shipBody.position;
            safeRotation = shipBody.rotation;
        }
        hasSafePose = true;
        return true;
    }

    void ApplySoftCeiling(float height)
    {
        float acceleration = CalculateSoftCeilingAcceleration(
            height,
            flightCeiling,
            SoftCeilingBand,
            MaximumCeilingAcceleration);
        if (acceleration <= 0f)
            return;

        // This mission-volume force does not replace the modular vehicle's
        // mass, thrust, wheel, atmosphere or aerodynamic calculations.
        float upwardSpeed = Mathf.Max(0f, shipBody.velocity.y);
        shipBody.AddForce(
            Vector3.down * (acceleration + upwardSpeed * 1.8f),
            ForceMode.Acceleration);
    }

    void RecoverShip()
    {
        Vector3 recoveryPosition = hasSafePose
            ? safePosition
            : battleCenter + Vector3.up * 20f;
        Quaternion recoveryRotation = hasSafePose
            ? safeRotation
            : Quaternion.identity;
        shipBody.position = recoveryPosition;
        shipBody.rotation = recoveryRotation;
        shipBody.velocity = Vector3.zero;
        shipBody.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
        warningActive = false;
        lastResetTime = Time.unscaledTime;
    }

    void OnGUI()
    {
        string message = null;
        if (warningActive)
            message = "警告：正在接近高山边界或任务飞行高度上限";
        else if (Time.unscaledTime - lastResetTime < ResetNoticeDuration)
            message = "飞船跌出地形，已执行故障救援";
        if (string.IsNullOrEmpty(message))
            return;

        const float width = 420f;
        const float height = 42f;
        GUI.Box(
            new Rect((Screen.width - width) * 0.5f, 24f, width, height),
            message);
    }

    public static bool IsOutsideWarningBounds(
        Vector3 position,
        Vector3 center,
        float radius,
        float ceiling)
    {
        Vector2 planarDelta = new Vector2(
            position.x - center.x,
            position.z - center.z);
        return planarDelta.sqrMagnitude > radius * radius
               || position.y > ceiling - 20f;
    }

    // Kept for compatibility with diagnostics that used the old name. Hard
    // horizontal and upper bounds no longer trigger a reset.
    public static bool IsOutsideHardBounds(
        Vector3 position,
        Vector3 center,
        float radius,
        float ceiling,
        float lowerRecoveryDepth = LowerRecoveryDepth)
    {
        return RequiresEmergencyRecovery(
            position,
            center,
            lowerRecoveryDepth);
    }

    public static bool RequiresEmergencyRecovery(
        Vector3 position,
        Vector3 center,
        float lowerRecoveryDepth = LowerRecoveryDepth)
    {
        return position.y
            < center.y - Mathf.Max(1f, lowerRecoveryDepth);
    }

    public static float CalculateSoftCeilingAcceleration(
        float height,
        float ceiling,
        float softBand = SoftCeilingBand,
        float maximumAcceleration = MaximumCeilingAcceleration)
    {
        float band = Mathf.Max(1f, softBand);
        float normalized = Mathf.InverseLerp(
            ceiling - band,
            ceiling,
            height);
        normalized = normalized * normalized
            * (3f - 2f * normalized);
        float aboveCeiling = Mathf.Max(0f, height - ceiling);
        return normalized * Mathf.Max(0f, maximumAcceleration)
            + aboveCeiling * 0.45f;
    }
}
