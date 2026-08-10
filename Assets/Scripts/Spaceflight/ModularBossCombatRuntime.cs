using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ModularAssembly;
using SpacecraftEditor;
using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.ModularAssembly;

public enum ModularBossThrusterDirection
{
    Right,
    Left,
    Up,
    Down,
    Forward,
    Backward
}

public enum ModularBossObstacleState
{
    Pursuit = 0,
    LocalAvoidance = 1,
    BlockedCeaseFire = 2,
    RamCharge = 3,
    Recovery = 4,
    PlayerRamTelegraph = 5,
    PlayerRamCharge = 6,
    CoverBreachTelegraph = 7,
    CoverBreachCharge = 8,
    BacktrackEscape = 9
}

/// <summary>
/// Tunable, deterministic rules for the city hunt.  Keeping the decisions here
/// makes the difficulty contract testable without instantiating a full planet.
/// </summary>
public static class ModularBossCombatPolicy
{
    public const int AssemblyModuleLimit = 512;
    public const int AssemblyCpuLimit = ModuleCpuBudget.AbsoluteMaximum;
    public const float MinimumBossShotCadence = 0.065f;
    public const float ShieldBreakWindowSeconds = 8f;
    public const float ShieldBreakWeaponStaggerSeconds = 0.9f;
    public const float ShieldRechargeFraction = 0.35f;
    public const float ShieldRechargeHullLockoutRatio = 0.35f;
    public const float ShieldMaximumWeaponDamagePerFrameFraction = 0.20f;
    public const float PursuitForceMultiplier = 1.18f;
    public const float PlayerRamTelegraphSeconds = 0.62f;
    public const float PlayerRamChargeSeconds = 1.55f;
    public const float PlayerRamRecoverySeconds = 0.90f;
    public const float PlayerRamCooldownSeconds = 4.4f;
    public const float CoverBreachTelegraphSeconds = 0.78f;
    public const float CoverBreachChargeSeconds = 2.35f;
    public const float CoverBreachCooldownSeconds = 7.5f;
    public const float BuildingStuckBreachSeconds = 2.75f;
    public const float BuildingStuckProgressDistance = 3f;
    public const float AvoidanceMaximumSeconds = 6f;
    public const float VerticalEscapeSeconds = 2.2f;
    public const float MinimumVerticalEscapeClearance = 0.28f;
    public const int SafeNavigationTrailCapacity = 12;
    public const float SafeNavigationPointSpacing = 12f;
    public const float BacktrackEscapeSeconds = 8f;
    public const float BacktrackMinimumTargetDistance = 18f;
    public const float BacktrackArrivalDistance = 6f;
    public const float CoverMemorySeconds = 0.42f;
    public const float NearbyCoverRadius = 52f;
    public const float EntrenchedCoverRadius = 48f;
    public const int PlayerCoverSampleCount = 7;
    public const float MaximumStableAngularSpeed = 1.18f;
    public const float MinimumDamagedMobility = 0.28f;

    public static float InitialSpawnDistance(int difficultyTier)
    {
        return Mathf.Lerp(
            310f,
            270f,
            Mathf.Clamp01(difficultyTier / 5f));
    }

    public static float PlayerRamDistance(int difficultyTier)
    {
        return Mathf.Lerp(
            76f,
            92f,
            Mathf.Clamp01(difficultyTier / 5f));
    }

    public static float CoverBreachDelay(int difficultyTier)
    {
        return Mathf.Lerp(
            7.2f,
            4.8f,
            Mathf.Clamp01(difficultyTier / 5f));
    }

    public static float ResolveWeaponSpread(
        float baseSpread,
        bool directLineBlocked,
        bool nearbyUrbanCover,
        float distance)
    {
        float coverMultiplier = directLineBlocked
            ? 3.15f
            : nearbyUrbanCover
                ? 2.05f
                : 0.68f;
        float distanceMultiplier = Mathf.Lerp(
            1f,
            1.24f,
            Mathf.InverseLerp(240f, 620f, distance));
        return Mathf.Clamp(
            baseSpread * coverMultiplier * distanceMultiplier,
            0.004f,
            0.14f);
    }

    public static float ResolveBossShotCadence(
        float shotsPerSecond,
        int difficultyTier,
        int liveWeaponCount)
    {
        float baseCadence = Mathf.Max(
            0.22f,
            1f / Mathf.Max(0.1f, shotsPerSecond));
        float tierCadence = baseCadence * Mathf.Lerp(
            1.35f,
            0.78f,
            Mathf.Clamp01(difficultyTier / 5f));

        // One surviving weapon keeps the original firing pace. Additional
        // weapons increase aggregate pressure without multiplying it linearly,
        // so weapon loss creates meaningful, stable firepower phases.
        float weaponPressure = Mathf.Sqrt(Mathf.Max(1, liveWeaponCount));
        return Mathf.Max(
            MinimumBossShotCadence,
            tierCadence / weaponPressure);
    }

    public static float ResolveShieldCapacity(int difficultyTier)
    {
        // Explosive player weapons apply both their direct hit and their
        // explosion to the shield. The current two-cannon starter-scale ship
        // therefore reaches roughly 660 burst DPS before any metagame damage
        // or fire-rate bonuses. Keep direct fire as a viable route, but give
        // city impacts enough time value to remain a meaningful shortcut.
        return Mathf.Lerp(
            6000f,
            15000f,
            Mathf.Clamp01(difficultyTier / 5f));
    }

    public static int ResolveShieldRechargeCount(int difficultyTier)
    {
        if (difficultyTier >= 4)
            return 2;
        return difficultyTier >= 2 ? 1 : 0;
    }

    public static float ResolveShieldWeaponDamagePerFrameCap(
        float maximumShieldIntegrity)
    {
        return Mathf.Max(0f, maximumShieldIntegrity) *
               ShieldMaximumWeaponDamagePerFrameFraction;
    }

    public static float ResolveBuildingShieldDamageFraction(float impactSpeed)
    {
        return Mathf.Lerp(
            0.30f,
            0.40f,
            Mathf.InverseLerp(14f, 52f, impactSpeed));
    }

    public static float ResolveBridgeShieldDamageFraction(float impactSpeed)
    {
        return Mathf.Lerp(
            0.45f,
            0.60f,
            Mathf.InverseLerp(8f, 40f, impactSpeed));
    }

    public static float ResolvePlayerRamDamage(
        float maximumModuleIntegrity,
        float relativeSpeed,
        int difficultyTier)
    {
        float speed01 = Mathf.InverseLerp(12f, 52f, relativeSpeed);
        float tier01 = Mathf.Clamp01(difficultyTier / 5f);
        float fraction = Mathf.Lerp(0.22f, 0.48f, speed01) *
                         Mathf.Lerp(0.88f, 1.12f, tier01);
        return Mathf.Clamp(
            Mathf.Max(1f, maximumModuleIntegrity) * fraction,
            18f,
            190f);
    }

    public static int ResolveBossCollisionModuleLoss(float relativeSpeed)
    {
        if (relativeSpeed >= 48f)
            return 3;
        if (relativeSpeed >= 31f)
            return 2;
        return relativeSpeed >= 17f ? 1 : 0;
    }

    public static bool IsPlayerEntrenched(
        bool coverRight,
        bool coverLeft,
        bool coverForward,
        bool coverBack)
    {
        return (coverRight && coverLeft) ||
               (coverForward && coverBack);
    }

    public static bool ShouldAuthorizeCoverBreach(
        bool directLineBlockedByLiveBuilding,
        bool coverPersisted)
    {
        return directLineBlockedByLiveBuilding && coverPersisted;
    }

    public static float ScorePlayerCoverBuilding(
        int occludedPlayerSamples,
        float distanceFromPlayer,
        float normalizedSightlineDepth)
    {
        // Covering more of the player's silhouette is the primary signal.
        // For equal coverage, prefer the building closest to the player rather
        // than an unrelated foreground building closer to the Boss.
        return Mathf.Max(0, occludedPlayerSamples) * 10000f +
               Mathf.Clamp01(normalizedSightlineDepth) * 1000f -
               Mathf.Clamp(distanceFromPlayer, 0f, 999f);
    }

    public static float ResolveDamagedMobility(
        int liveThrusters,
        int initialThrusters)
    {
        float ratio = initialThrusters <= 0
            ? 0f
            : Mathf.Clamp01(liveThrusters / (float)initialThrusters);
        return Mathf.Lerp(MinimumDamagedMobility, 1f, ratio);
    }
}

/// <summary>
/// Selects a deterministic, wide-road Boss start rather than placing the
/// five-times-large assembly in an arbitrary city volume.
/// </summary>
public static class ModularBossRoadSpawnPolicy
{
    public static bool TryResolve(
        IReadOnlyList<AirCombatRoadStrip> roads,
        Vector3 playerPlanPosition,
        float preferredDistance,
        out Vector3 spawnPlanPosition,
        out Vector3 roadDirection)
    {
        spawnPlanPosition = Vector3.zero;
        roadDirection = Vector3.forward;
        if (roads == null || roads.Count == 0)
            return false;

        float targetDistance = Mathf.Max(80f, preferredDistance);
        float bestScore = float.NegativeInfinity;
        bool found = false;
        for (int roadIndex = 0; roadIndex < roads.Count; roadIndex++)
        {
            AirCombatRoadStrip road = roads[roadIndex];
            if (road == null || road.width < 22f)
                continue;
            Vector3 segment = road.end - road.start;
            segment.y = 0f;
            float length = segment.magnitude;
            if (length < 1f)
                continue;
            Vector3 direction = segment / length;
            for (int sampleIndex = 1; sampleIndex <= 9; sampleIndex++)
            {
                float t = sampleIndex / 10f;
                Vector3 candidate = Vector3.Lerp(
                    road.start,
                    road.end,
                    t);
                candidate.y = 0f;
                float distance = Vector3.Distance(
                    new Vector3(
                        playerPlanPosition.x,
                        0f,
                        playerPlanPosition.z),
                    candidate);
                float distanceScore = 1f - Mathf.Clamp01(
                    Mathf.Abs(distance - targetDistance) /
                    targetDistance);
                float mainRoadBonus = road.kind == AirCombatRouteKind.Main
                    ? 2.4f
                    : 0f;
                float widthScore = Mathf.Clamp(road.width / 66f, 0f, 1.5f);
                float laneScore = Mathf.Clamp(road.laneTiles, 1, 3) * 0.35f;
                float endpointClearance = Mathf.Min(t, 1f - t) * 0.4f;
                float score = mainRoadBonus + widthScore + laneScore +
                              distanceScore * 2f + endpointClearance;
                if (score <= bestScore)
                    continue;
                bestScore = score;
                spawnPlanPosition = candidate;
                roadDirection = direction;
                found = true;
            }
        }
        return found;
    }
}

public static class ModularBossObstaclePolicy
{
    public const float SustainedBridgeContactSeconds = 0.25f;
    public const float AvoidanceAttemptSeconds = 1f;
    public const float RamChargeSeconds = 1f;
    public const float RecoverySeconds = 0.6f;
    public const float CeaseFireDecay = 0.72f;
    public const float MinimumCeaseFireSeconds = 1f;
    public const float RamImmunitySeconds = 14f;
    public const float ProbeOnlyApproachSeconds = 1.35f;

    public static bool ShouldConfirmBridgeContact(
        float contactStartedAt,
        float now)
    {
        return contactStartedAt >= 0f &&
               now - contactStartedAt >= SustainedBridgeContactSeconds;
    }

    public static bool ShouldCommitRam(
        float contactConfirmedAt,
        float now,
        bool sameBridgeStillBlocking)
    {
        return contactConfirmedAt >= 0f &&
               sameBridgeStillBlocking &&
               now - contactConfirmedAt >= AvoidanceAttemptSeconds;
    }

    public static bool ShouldForceProbedBridgeApproach(
        float avoidanceStartedAt,
        float now,
        bool hasRecentContact,
        bool hasDirectBridgeProbe)
    {
        return !hasRecentContact &&
               hasDirectBridgeProbe &&
               now - avoidanceStartedAt >= ProbeOnlyApproachSeconds;
    }

    public static float FirstCeaseFireSeconds(int difficultyTier)
    {
        return Mathf.Lerp(
            4.5f,
            2.5f,
            Mathf.Clamp01(difficultyTier / 5f));
    }

    public static float ResolveCeaseFireSeconds(
        int difficultyTier,
        int previousWindows)
    {
        return Mathf.Max(
            MinimumCeaseFireSeconds,
            FirstCeaseFireSeconds(difficultyTier) *
            Mathf.Pow(CeaseFireDecay, Mathf.Max(0, previousWindows)));
    }

    public static bool IsBridgeBetweenBossAndPlayer(
        Vector3 bossPosition,
        Vector3 playerPosition,
        Bounds bridgeBounds,
        float lateralTolerance)
    {
        Vector3 pursuit = playerPosition - bossPosition;
        float distance = pursuit.magnitude;
        if (distance < 0.01f)
            return false;
        Vector3 direction = pursuit / distance;
        float along = Vector3.Dot(
            bridgeBounds.center - bossPosition,
            direction);
        if (along <= 0f || along >= distance)
            return false;
        Vector3 closestOnPursuit = bossPosition + direction * along;
        Vector3 closestOnBridge = bridgeBounds.ClosestPoint(
            closestOnPursuit);
        return Vector3.Distance(
                   closestOnPursuit,
                   closestOnBridge) <= Mathf.Max(0f, lateralTolerance);
    }
}

public static class ModularBossSteeringPolicy
{
    /// <summary>
    /// Converts a world-space movement demand to the aim-relative axes used
    /// by RobocraftControlFrame: x=strafe, y=vertical, z=forward.
    /// </summary>
    public static Vector3 ToAimRelativeAxes(
        Vector3 worldDemand,
        Vector3 aimForward,
        Vector3 up)
    {
        up = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(aimForward, up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(Vector3.forward, up);
        forward.Normalize();
        Vector3 right = Vector3.Cross(up, forward).normalized;
        return new Vector3(
            Vector3.Dot(worldDemand, right),
            Vector3.Dot(worldDemand, up),
            Vector3.Dot(worldDemand, forward));
    }
}

public sealed class ModularBossGenerationProfile
{
    public int Tier { get; private set; }
    public int HullRadius { get; private set; }
    public int CoreCoverDepth => HullRadius - 1;
    public int ThrustersPerDirection { get; private set; }
    public int WeaponCount { get; private set; }
    public float StructureIntegrityMultiplier { get; private set; }
    public float CoreIntegrityMultiplier { get; private set; }
    public float SystemIntegrityMultiplier { get; private set; }
    public float PreferredCombatRadius { get; private set; }

    public static ModularBossGenerationProfile ForTier(int planetTier)
    {
        int tier = Mathf.Clamp(
            planetTier,
            0,
            ProceduralInterstellarGenerator.StarterSystemPlanetCount - 1);
        return new ModularBossGenerationProfile
        {
            Tier = tier,
            HullRadius = 2 + tier / 3,
            // Always use opposite pairs. Odd counts create a permanent
            // translation torque on a five-times-large rigidbody.
            ThrustersPerDirection = 2 + (tier / 2) * 2,
            WeaponCount = 2 + (tier / 2) * 2,
            StructureIntegrityMultiplier = 1f + tier * 0.18f,
            CoreIntegrityMultiplier = 1f + tier * 0.12f,
            SystemIntegrityMultiplier = 1f + tier * 0.06f,
            PreferredCombatRadius = Mathf.Max(82f, 120f - tier * 6f)
        };
    }
}

public sealed class ModularBossBuildResult
{
    readonly Dictionary<string, ModularBossThrusterDirection>
        thrusterDirections;

    public GridAssemblyModel Model { get; }
    public ModularBossGenerationProfile Profile { get; }
    public IReadOnlyDictionary<string, ModularBossThrusterDirection>
        ThrusterDirections => thrusterDirections;
    public int StructureModuleCount { get; }
    public int WeaponCount { get; }

    public ModularBossBuildResult(
        GridAssemblyModel model,
        ModularBossGenerationProfile profile,
        Dictionary<string, ModularBossThrusterDirection> directions,
        int structureModuleCount,
        int weaponCount)
    {
        Model = model;
        Profile = profile;
        thrusterDirections = directions;
        StructureModuleCount = structureModuleCount;
        WeaponCount = weaponCount;
    }

    public int CountThrusters(ModularBossThrusterDirection direction) =>
        thrusterDirections.Values.Count(value => value == direction);
}

/// <summary>
/// Boss modules keep the same graph cells and definitions as the player, but
/// their complete physical presentation is five times larger.  Scaling each
/// module instead of the Rigidbody root preserves RC3's unit-root invariant
/// while also scaling colliders, muzzles, exhaust sockets and detached views.
/// </summary>
public static class ModularBossModuleScalePolicy
{
    public const float LinearScale = 5f;

    public static void Apply(GridAssemblyPresenter presenter)
    {
        if (presenter == null)
            return;
        foreach (GridModuleView view in presenter.Views.Values)
            Apply(view);
    }

    public static void Apply(GridModuleView view)
    {
        GridModuleRecord record = view?.Record;
        if (record == null)
            return;
        view.transform.localPosition =
            GridAssemblyModel.ModuleCenter(record) * LinearScale;
        view.transform.localScale = Vector3.one * LinearScale;
    }
}

public static class ModularBossFlightAuthorityPolicy
{
    public const float MinimumManeuverAcceleration = 6f;
    public const float NetLiftAcceleration = 4f;
    public const float MaximumMultiplier = 64f;

    public static Vector3 ResolveEmergencyCoreForce(
        float totalMass,
        float gravityMagnitude)
    {
        float mass = Mathf.Max(1f, totalMass);
        float gravity = Mathf.Max(0f, gravityMagnitude);
        return new Vector3(
            mass * 7f,
            mass * (gravity + 5f),
            mass * 7f);
    }

    public static float ResolveEmergencyDownForce(float totalMass)
    {
        return Mathf.Max(1f, totalMass) * 6f;
    }

    public static float ResolveMultiplier(
        float totalMass,
        float gravityMagnitude,
        IReadOnlyList<VehicleActuatorDiagnostic> actuators)
    {
        float mass = Mathf.Max(1f, totalMass);
        float gravity = Mathf.Max(0f, gravityMagnitude);
        float required = 1f;
        required = Mathf.Max(required, RequiredScale(
            actuators,
            Vector3.up,
            mass * (gravity + NetLiftAcceleration)));
        required = Mathf.Max(required, RequiredScale(
            actuators,
            Vector3.down,
            mass * MinimumManeuverAcceleration));
        required = Mathf.Max(required, RequiredScale(
            actuators,
            Vector3.right,
            mass * MinimumManeuverAcceleration));
        required = Mathf.Max(required, RequiredScale(
            actuators,
            Vector3.left,
            mass * MinimumManeuverAcceleration));
        required = Mathf.Max(required, RequiredScale(
            actuators,
            Vector3.forward,
            mass * MinimumManeuverAcceleration));
        required = Mathf.Max(required, RequiredScale(
            actuators,
            Vector3.back,
            mass * MinimumManeuverAcceleration));
        return Mathf.Clamp(required, 1f, MaximumMultiplier);
    }

    public static bool HasMinimumAuthority(
        float totalMass,
        float gravityMagnitude,
        IReadOnlyList<VehicleActuatorDiagnostic> actuators)
    {
        float mass = Mathf.Max(1f, totalMass);
        float gravity = Mathf.Max(0f, gravityMagnitude);
        return AvailableForce(actuators, Vector3.up) >=
                   mass * (gravity + NetLiftAcceleration) * 0.995f &&
               AvailableForce(actuators, Vector3.down) >=
                   mass * MinimumManeuverAcceleration * 0.995f &&
               AvailableForce(actuators, Vector3.right) >=
                   mass * MinimumManeuverAcceleration * 0.995f &&
               AvailableForce(actuators, Vector3.left) >=
                   mass * MinimumManeuverAcceleration * 0.995f &&
               AvailableForce(actuators, Vector3.forward) >=
                   mass * MinimumManeuverAcceleration * 0.995f &&
               AvailableForce(actuators, Vector3.back) >=
                   mass * MinimumManeuverAcceleration * 0.995f;
    }

    static float RequiredScale(
        IReadOnlyList<VehicleActuatorDiagnostic> actuators,
        Vector3 direction,
        float requiredForce)
    {
        float available = AvailableForce(actuators, direction);
        return available > 0.01f
            ? requiredForce / available
            : MaximumMultiplier;
    }

    static float AvailableForce(
        IReadOnlyList<VehicleActuatorDiagnostic> actuators,
        Vector3 direction)
    {
        if (actuators == null)
            return 0f;
        float total = 0f;
        for (int index = 0; index < actuators.Count; index++)
        {
            VehicleActuatorDiagnostic actuator = actuators[index];
            total += Mathf.Max(
                         0f,
                         Vector3.Dot(
                             actuator.localForceDirection,
                             direction)) *
                     Mathf.Max(0f, actuator.effectiveMaximumForce);
        }
        return total;
    }
}

[DisallowMultipleComponent]
public sealed class ModularBossReadabilityPresentation : MonoBehaviour
{
    static readonly Color HullColor =
        new Color(0.52f, 0.30f, 0.68f, 1f);
    static readonly Color HullEmission =
        new Color(0.16f, 0.035f, 0.24f, 1f);
    static readonly Color CoreColor =
        new Color(1f, 0.56f, 0.10f, 1f);
    static readonly Color CoreEmission =
        new Color(1.6f, 0.38f, 0.025f, 1f);
    static readonly Color ThrusterColor =
        new Color(0.16f, 0.72f, 1f, 1f);
    static readonly Color ThrusterEmission =
        new Color(0.03f, 0.58f, 1.45f, 1f);
    static readonly Color WeaponColor =
        new Color(1f, 0.22f, 0.08f, 1f);
    static readonly Color WeaponEmission =
        new Color(1.25f, 0.06f, 0.015f, 1f);

    MaterialPropertyBlock paint;
    GridAssemblyPresenter presenter;
    ModularBossBuildResult build;
    Light keyLight;
    Light fillLight;
    bool ramWarningActive;

    void Awake()
    {
        EnsurePaint();
    }

    public void Configure(
        GridAssemblyPresenter sourcePresenter,
        ModularBossBuildResult sourceBuild)
    {
        EnsurePaint();
        if (presenter != null)
            presenter.Rebuilt -= Refresh;
        presenter = sourcePresenter;
        build = sourceBuild;
        if (presenter != null)
            presenter.Rebuilt += Refresh;
        Refresh();
    }

    public void SetRamWarning(bool active)
    {
        if (ramWarningActive == active && keyLight != null && fillLight != null)
            return;
        ramWarningActive = active;
        RefreshFillLights();
    }

    public void StopTrackingPresenterRebuilds()
    {
        if (presenter != null)
            presenter.Rebuilt -= Refresh;
    }

    void Refresh()
    {
        if (presenter == null)
            return;
        EnsurePaint();
        foreach (GridModuleView view in presenter.Views.Values)
        {
            if (view?.Record == null)
                continue;
            ResolveColors(view, out Color color, out Color emission);
            foreach (Renderer renderer in
                     view.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;
                paint.Clear();
                Material material = renderer.sharedMaterial;
                if (material != null && material.HasProperty("_BaseColor"))
                    paint.SetColor("_BaseColor", color);
                if (material != null && material.HasProperty("_Color"))
                    paint.SetColor("_Color", color);
                if (material != null &&
                    material.HasProperty("_EmissionColor"))
                {
                    paint.SetColor("_EmissionColor", emission);
                }
                renderer.SetPropertyBlock(paint);
            }
            foreach (NeoXThrusterExhaustVfx exhaust in
                     view.GetComponentsInChildren<NeoXThrusterExhaustVfx>(true))
            {
                exhaust.SetPresentationTuning(0.58f, 0.46f);
            }
        }
        RefreshFillLights();
    }

    void EnsurePaint()
    {
        if (paint == null)
            paint = new MaterialPropertyBlock();
    }

    void ResolveColors(
        GridModuleView view,
        out Color color,
        out Color emission)
    {
        if (string.Equals(
                view.Record.RuntimeId,
                GridAssemblyModel.CoreRuntimeId,
                StringComparison.Ordinal))
        {
            color = CoreColor;
            emission = CoreEmission;
            return;
        }
        if (build != null && build.ThrusterDirections.ContainsKey(
                view.Record.RuntimeId))
        {
            color = ThrusterColor;
            emission = ThrusterEmission;
            return;
        }
        if (WeaponProfileLibrary.IsWeapon(view))
        {
            color = WeaponColor;
            emission = WeaponEmission;
            return;
        }
        color = HullColor;
        emission = HullEmission;
    }

    void RefreshFillLights()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            if (renderers[index] != null)
                bounds.Encapsulate(renderers[index].bounds);
        keyLight = EnsureLight(
            keyLight,
            "BossReadabilityKey",
            ramWarningActive
                ? new Color(1f, 0.18f, 0.035f)
                : new Color(0.48f, 0.68f, 1f),
            ramWarningActive ? 3.1f : 1.85f);
        fillLight = EnsureLight(
            fillLight,
            "BossReadabilityFill",
            ramWarningActive
                ? new Color(1f, 0.54f, 0.08f)
                : new Color(0.78f, 0.34f, 1f),
            ramWarningActive ? 2.25f : 1.15f);
        if (keyLight == null || fillLight == null)
            return;
        float offset = Mathf.Max(8f, bounds.extents.magnitude * 0.72f);
        keyLight.transform.position = bounds.center +
                                      Vector3.up * offset +
                                      transform.forward * offset;
        fillLight.transform.position = bounds.center -
                                       Vector3.up * (offset * 0.35f) -
                                       transform.forward * offset;
        float range = Mathf.Max(36f, bounds.size.magnitude * 1.05f);
        keyLight.range = range;
        fillLight.range = range;
    }

    Light EnsureLight(
        Light current,
        string lightName,
        Color color,
        float intensity)
    {
        GameObject root = null;
        if (current != null)
            root = current.gameObject;
        if (root == null)
        {
            Transform existing = transform.Find(lightName);
            root = existing != null
                ? existing.gameObject
                : new GameObject(lightName);
            root.transform.SetParent(transform, true);
        }
        Light attached = root.GetComponent<Light>();
        if (attached == null)
            attached = root.AddComponent<Light>();
        current = attached;
        if (current == null)
            return null;
        current.type = LightType.Point;
        current.color = color;
        current.intensity = intensity;
        current.shadows = LightShadows.None;
        current.renderMode = LightRenderMode.Auto;
        return current;
    }

    void OnDestroy()
    {
        if (presenter != null)
            presenter.Rebuilt -= Refresh;
    }
}

[DisallowMultipleComponent]
public sealed class ModularBossShieldRuntime : MonoBehaviour
{
    const float EnvironmentalImpactCooldownSeconds = 0.85f;
    const float BreakVisualSeconds = 0.55f;

    VehicleStructureGraph structureGraph;
    GridAssemblyPresenter presenter;
    Transform shell;
    MeshRenderer shellRenderer;
    Material shellMaterial;
    float maximumIntegrity;
    float integrity;
    float rechargeAt = -1f;
    float staggerUntil = -1f;
    float breakVisualUntil = -1f;
    float nextEnvironmentalImpactAt = -1f;
    float hitFlash;
    float visualOpacity;
    int remainingRecharges;
    int breakFrame = -1;
    int weaponDamageFrame = -1;
    float weaponDamageAbsorbedThisFrame;
    bool combatActive;
    bool permanentlyOffline;
    bool boundsDirty;

    public float Integrity => integrity;
    public float MaximumIntegrity => maximumIntegrity;
    public float IntegrityRatio => maximumIntegrity <= 0.01f
        ? 0f
        : Mathf.Clamp01(integrity / maximumIntegrity);
    public bool IsActive => integrity > 0.01f;
    public bool IsBreakWeaponStaggerActive => Time.time < staggerUntil;
    public int RemainingRecharges => remainingRecharges;
    public string StatusLabel
    {
        get
        {
            if (IsActive)
            {
                return $"护盾 {IntegrityRatio:P0} · " +
                       $"剩余回充 {remainingRecharges}";
            }
            if (!permanentlyOffline && rechargeAt > Time.time)
                return $"护盾破裂 · 暴露 {rechargeAt - Time.time:0.0}秒";
            return "护盾离线 · 模块完全暴露";
        }
    }

    public void Configure(
        VehicleStructureGraph graph,
        GridAssemblyPresenter assemblyPresenter,
        int difficultyTier)
    {
        if (presenter != null)
            presenter.Rebuilt -= HandlePresenterRebuilt;
        structureGraph = graph;
        presenter = assemblyPresenter;
        maximumIntegrity = ModularBossCombatPolicy.ResolveShieldCapacity(
            difficultyTier);
        integrity = maximumIntegrity;
        remainingRecharges = ModularBossCombatPolicy.
            ResolveShieldRechargeCount(difficultyTier);
        rechargeAt = -1f;
        staggerUntil = -1f;
        breakVisualUntil = -1f;
        permanentlyOffline = false;
        breakFrame = -1;
        weaponDamageFrame = -1;
        weaponDamageAbsorbedThisFrame = 0f;
        boundsDirty = false;
        EnsureVisual();
        if (presenter != null)
            presenter.Rebuilt += HandlePresenterRebuilt;
        RefreshBounds();
        RefreshVisual(true);
    }

    public void SetCombatActive(bool value)
    {
        combatActive = value;
    }

    public SpaceDamageInfo FilterIncomingDamage(
        string runtimeId,
        SpaceDamageInfo incoming)
    {
        if (incoming.amount <= 0f)
            return incoming;
        if (!IsActive)
        {
            // When one explosion breaks the shield, only its breaking hit may
            // overflow. Other colliders reached by that same frame are blocked
            // so a multi-collider explosion cannot multiply the overflow.
            return Time.frameCount == breakFrame
                ? CloneWithAmount(incoming, 0f)
                : incoming;
        }

        float eligibleDamage = incoming.amount;
        bool weaponDamage = incoming.type == SpaceDamageType.Projectile ||
                            incoming.type == SpaceDamageType.Explosion;
        if (weaponDamage)
        {
            if (weaponDamageFrame != Time.frameCount)
            {
                weaponDamageFrame = Time.frameCount;
                weaponDamageAbsorbedThisFrame = 0f;
            }
            float frameBudget =
                ModularBossCombatPolicy.ResolveShieldWeaponDamagePerFrameCap(
                    maximumIntegrity);
            eligibleDamage = Mathf.Min(
                eligibleDamage,
                Mathf.Max(0f, frameBudget -
                               weaponDamageAbsorbedThisFrame));
            if (eligibleDamage <= 0.0001f)
                return CloneWithAmount(incoming, 0f);
        }

        float absorbed = Mathf.Min(integrity, eligibleDamage);
        integrity = Mathf.Max(0f, integrity - absorbed);
        if (weaponDamage)
            weaponDamageAbsorbedThisFrame += absorbed;
        hitFlash = 1f;
        ReportAbsorbedDamage(incoming, absorbed);
        float remainder = Mathf.Max(0f, eligibleDamage - absorbed);
        if (integrity <= 0.01f)
            BreakShield();
        return CloneWithAmount(incoming, remainder);
    }

    public bool ApplyEnvironmentalImpact(
        float shieldFraction,
        Vector3 point,
        GameObject source)
    {
        if (!IsActive)
            return false;
        if (Time.time < nextEnvironmentalImpactAt)
            return true;
        nextEnvironmentalImpactAt = Time.time +
                                    EnvironmentalImpactCooldownSeconds;
        float damage = maximumIntegrity * Mathf.Clamp01(shieldFraction);
        SpaceDamageInfo impact = new SpaceDamageInfo(
            damage,
            point,
            Vector3.zero,
            SpaceDamageType.Collision,
            source);
        float absorbed = Mathf.Min(integrity, damage);
        integrity = Mathf.Max(0f, integrity - absorbed);
        hitFlash = 1f;
        ReportAbsorbedDamage(impact, absorbed);
        if (integrity <= 0.01f)
            BreakShield();
        // A collision that begins against an active shield is fully caught by
        // that layer. Later unshielded collisions retain the original module
        // loss behavior.
        return true;
    }

    void Update()
    {
        if (combatActive && !IsActive && !permanentlyOffline &&
            rechargeAt >= 0f && Time.time >= rechargeAt)
        {
            TryRecharge();
        }
        RefreshVisual(false);
    }

    void TryRecharge()
    {
        if (remainingRecharges <= 0 || structureGraph == null ||
            structureGraph.IsVehicleDestroyed ||
            structureGraph.OverallHealthRatio <=
            ModularBossCombatPolicy.ShieldRechargeHullLockoutRatio)
        {
            permanentlyOffline = true;
            rechargeAt = -1f;
            return;
        }

        remainingRecharges--;
        if (boundsDirty)
            RefreshBounds();
        integrity = maximumIntegrity *
                    ModularBossCombatPolicy.ShieldRechargeFraction;
        rechargeAt = -1f;
        hitFlash = 1f;
        visualOpacity = 0f;
        RefreshVisual(true);
    }

    void BreakShield()
    {
        integrity = 0f;
        breakFrame = Time.frameCount;
        staggerUntil = Time.time +
                       ModularBossCombatPolicy.ShieldBreakWeaponStaggerSeconds;
        breakVisualUntil = Time.time + BreakVisualSeconds;
        if (remainingRecharges > 0 && structureGraph != null &&
            structureGraph.OverallHealthRatio >
            ModularBossCombatPolicy.ShieldRechargeHullLockoutRatio)
        {
            rechargeAt = Time.time +
                         ModularBossCombatPolicy.ShieldBreakWindowSeconds;
        }
        else
        {
            permanentlyOffline = true;
            rechargeAt = -1f;
        }
    }

    void ReportAbsorbedDamage(SpaceDamageInfo damage, float absorbed)
    {
        if (absorbed <= 0.0001f)
            return;
        VehicleCombatTeam sourceTeam = damage.source != null
            ? VehicleCombatTeamUtility.Resolve(damage.source.transform)
            : VehicleCombatTeam.Neutral;
        CombatDamageFeedbackBus.Report(new CombatDamageAppliedFeedback(
            sourceTeam,
            VehicleCombatTeam.Enemy,
            damage.point,
            absorbed,
            false));
    }

    static SpaceDamageInfo CloneWithAmount(
        SpaceDamageInfo source,
        float amount)
    {
        return new SpaceDamageInfo(
            amount,
            source.point,
            source.impulse,
            source.type,
            source.channel,
            source.source);
    }

    void EnsureVisual()
    {
        if (shell != null && shellRenderer != null && shellMaterial != null)
            return;
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "BossEnergyShield";
        visual.transform.SetParent(transform, false);
        Collider primitiveCollider = visual.GetComponent<Collider>();
        if (primitiveCollider != null)
        {
            primitiveCollider.enabled = false;
            if (Application.isPlaying)
                Destroy(primitiveCollider);
            else
                DestroyImmediate(primitiveCollider);
        }
        shell = visual.transform;
        shellRenderer = visual.GetComponent<MeshRenderer>();
        if (shellRenderer == null)
            return;
        shellRenderer.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        shellRenderer.receiveShadows = false;
        Shader shader = Resources.Load<Shader>(
            "Shaders/BossEnergyShield");
        if (shader == null)
            shader = Shader.Find("UnityPlanet/BossEnergyShield");
        if (shader == null)
        {
            Debug.LogWarning(
                "[ModularBossShield] BossEnergyShield shader was not found.",
                this);
            shellRenderer.enabled = false;
            return;
        }
        shellMaterial = new Material(shader)
        {
            name = "BossEnergyShield_Runtime",
            hideFlags = HideFlags.DontSave
        };
        Texture2D pattern = Resources.Load<Texture2D>(
            "Boss/ShieldHexEnergy");
        if (pattern != null)
        {
            pattern.wrapMode = TextureWrapMode.Repeat;
            pattern.filterMode = FilterMode.Bilinear;
            shellMaterial.SetTexture("_MainTex", pattern);
        }
        shellRenderer.sharedMaterial = shellMaterial;
    }

    void RefreshBounds()
    {
        if (shell == null || structureGraph == null)
            return;
        Bounds bounds = structureGraph.ResolveVisualBounds();
        if (bounds.size.sqrMagnitude <= 0.01f)
            return;
        shell.localPosition = transform.InverseTransformPoint(bounds.center);
        Vector3 lossyScale = transform.lossyScale;
        Vector3 worldSize = bounds.size * 1.14f + Vector3.one * 2.5f;
        shell.localScale = new Vector3(
            worldSize.x / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.x)),
            worldSize.y / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.y)),
            worldSize.z / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.z)));
        boundsDirty = false;
    }

    void HandlePresenterRebuilt()
    {
        // Hull modules cannot be removed through an active shield. While the
        // shield is broken, defer the O(module count) bounds scan until the
        // instant a recharge actually needs the shell again.
        if (IsActive)
            RefreshBounds();
        else
            boundsDirty = true;
    }

    void RefreshVisual(bool immediate)
    {
        if (shellRenderer == null || shellMaterial == null)
            return;
        float breakFade = breakVisualUntil <= Time.time
            ? 0f
            : Mathf.InverseLerp(
                breakVisualUntil,
                breakVisualUntil - BreakVisualSeconds,
                Time.time);
        float targetOpacity = IsActive
            ? Mathf.Lerp(0.48f, 0.68f, IntegrityRatio)
            : breakFade * 0.82f;
        visualOpacity = immediate
            ? targetOpacity
            : Mathf.MoveTowards(
                visualOpacity,
                targetOpacity,
                Time.unscaledDeltaTime * 2.8f);
        hitFlash = immediate
            ? hitFlash
            : Mathf.MoveTowards(
                hitFlash,
                0f,
                Time.unscaledDeltaTime * 2.6f);
        shellMaterial.SetFloat("_Integrity", IntegrityRatio);
        shellMaterial.SetFloat("_Opacity", visualOpacity);
        shellMaterial.SetFloat("_HitFlash", hitFlash);
        shellRenderer.enabled = visualOpacity > 0.005f;
    }

    void OnDestroy()
    {
        if (presenter != null)
            presenter.Rebuilt -= HandlePresenterRebuilt;
        if (shellMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(shellMaterial);
            else
                DestroyImmediate(shellMaterial);
        }
    }
}

/// <summary>
/// Builds a connected, symmetric six-axis ship from the same module
/// definitions used by the player. The core remains hidden by actual grid
/// blocks after the separate combat shield has been broken.
/// </summary>
public static class ModularBossPcgGenerator
{
    static readonly Vector3Int[] ForceDirections =
    {
        Vector3Int.right,
        Vector3Int.left,
        Vector3Int.up,
        Vector3Int.down,
        Vector3Int.forward,
        Vector3Int.back
    };

    public static bool TryBuild(
        IEnumerable<GridModuleDefinition> sourceDefinitions,
        int planetTier,
        int seed,
        out ModularBossBuildResult result,
        out string error)
    {
        result = null;
        error = string.Empty;
        GridModuleDefinition[] definitions =
            (sourceDefinitions ?? Array.Empty<GridModuleDefinition>())
            .Where(item => item != null)
            .ToArray();
        if (!TryResolveDefinition(
                definitions,
                GridModuleCategory.Structure,
                "block_111",
                out GridModuleDefinition structure,
                requireSingleCell: true) ||
            !TryResolveThruster(definitions, out GridModuleDefinition thruster) ||
            !TryResolveDefinition(
                definitions,
                GridModuleCategory.KineticWeapon,
                "machinegun_111",
                out GridModuleDefinition weapon))
        {
            error =
                "Boss generation requires a one-cell structure block, a rocket thruster and a weapon.";
            return false;
        }

        var model = new GridAssemblyModel(
            definitions,
            ModularBossCombatPolicy.AssemblyModuleLimit,
            ModularBossCombatPolicy.AssemblyCpuLimit);
        if (model.Find(GridAssemblyModel.CoreRuntimeId) == null)
        {
            error = "Boss generation requires the player core definition.";
            return false;
        }

        ModularBossGenerationProfile profile =
            ModularBossGenerationProfile.ForTier(planetTier);
        int radius = profile.HullRadius;
        int structureCount = 0;
        var hullCells = new List<Vector3Int>();
        for (int x = -radius; x < radius; x++)
        for (int y = -radius; y < radius; y++)
        for (int z = -radius; z < radius; z++)
        {
            if (IsCoreCell(x, y, z))
                continue;
            hullCells.Add(new Vector3Int(x, y, z));
        }
        int plannedModuleCount = model.Records.Count +
                                 hullCells.Count +
                                 ForceDirections.Length *
                                 profile.ThrustersPerDirection +
                                 profile.WeaponCount;
        if (plannedModuleCount > model.EffectiveModuleLimit)
        {
            error =
                $"Boss generation requires {plannedModuleCount} modules, " +
                $"exceeding its independent limit " +
                $"{model.EffectiveModuleLimit}.";
            return false;
        }
        int plannedCpuCost =
            ModuleCpuBudget.Total(model.Records) +
            hullCells.Count * ModuleCpuBudget.Cost(structure) +
            ForceDirections.Length * profile.ThrustersPerDirection *
            ModuleCpuBudget.Cost(thruster) +
            profile.WeaponCount * ModuleCpuBudget.Cost(weapon);
        if (plannedCpuCost > model.EffectiveCpuLimit)
        {
            error =
                $"Boss generation requires {plannedCpuCost} CPU, " +
                $"exceeding its independent limit " +
                $"{model.EffectiveCpuLimit}.";
            return false;
        }
        foreach (Vector3Int cell in hullCells
                     .OrderBy(DistanceFromCore)
                     .ThenBy(item => item.y)
                     .ThenBy(item => item.z)
                     .ThenBy(item => item.x))
        {
            if (!model.TryPlace(
                    structure.ModuleId,
                    new GridModulePose(cell, 0),
                    false,
                    out _,
                    out string placementError))
            {
                error = "Boss hull generation failed: " + placementError;
                return false;
            }
            structureCount++;
        }

        var random = new System.Random(seed);
        var directions =
            new Dictionary<string, ModularBossThrusterDirection>(
                StringComparer.Ordinal);
        for (int index = 0; index < ForceDirections.Length; index++)
        {
            Vector3Int forceDirection = ForceDirections[index];
            Vector3Int outwardNormal = -forceDirection;
            List<Vector2Int> faceCoordinates =
                BuildFaceCoordinates(radius, random);
            int placed = 0;
            for (int pairIndex = 0;
                 pairIndex + 1 < faceCoordinates.Count &&
                 placed < profile.ThrustersPerDirection;
                 pairIndex += 2)
            {
                Vector3Int firstSurfaceCell = ResolveSurfaceCell(
                    outwardNormal,
                    faceCoordinates[pairIndex],
                    radius);
                if (!TryPlaceOnSurface(
                        model,
                        thruster,
                        firstSurfaceCell,
                        outwardNormal,
                        out string firstRuntimeId))
                {
                    continue;
                }
                Vector3Int secondSurfaceCell = ResolveSurfaceCell(
                    outwardNormal,
                    faceCoordinates[pairIndex + 1],
                    radius);
                if (!TryPlaceOnSurface(
                        model,
                        thruster,
                        secondSurfaceCell,
                        outwardNormal,
                        out string secondRuntimeId))
                {
                    model.TryRemove(firstRuntimeId, out _);
                    continue;
                }
                ModularBossThrusterDirection logicalDirection =
                    ToDirection(forceDirection);
                directions[firstRuntimeId] = logicalDirection;
                directions[secondRuntimeId] = logicalDirection;
                placed += 2;
            }
            if (placed < profile.ThrustersPerDirection)
            {
                error =
                    $"Boss could place only {placed}/" +
                    $"{profile.ThrustersPerDirection} thrusters for " +
                    ToDirection(forceDirection) + ".";
                return false;
            }
        }

        int weaponsPlaced = PlaceWeapons(
            model,
            weapon,
            radius,
            profile.WeaponCount,
            random);
        if (weaponsPlaced < profile.WeaponCount)
        {
            error =
                $"Boss could place only {weaponsPlaced}/" +
                $"{profile.WeaponCount} connected weapons.";
            return false;
        }

        model.SetCoreAssistMode(VehicleCoreAssistMode.Standard);
        GridAssemblyValidation validation = model.Validate();
        if (!validation.IsValid)
        {
            error = "Generated Boss blueprint is invalid: " +
                    validation.Message;
            return false;
        }

        result = new ModularBossBuildResult(
            model,
            profile,
            directions,
            structureCount,
            weaponsPlaced);
        return true;
    }

    static bool TryResolveThruster(
        IEnumerable<GridModuleDefinition> definitions,
        out GridModuleDefinition definition)
    {
        definition = definitions
            .Where(item =>
                item != null &&
                (item.Category == GridModuleCategory.RcsThruster ||
                 item.Category == GridModuleCategory.MainThruster))
            .OrderBy(item => PreferredIdScore(
                item.ModuleId,
                "speed_rocketsmall_112"))
            .ThenBy(item => CellCount(item.Footprint))
            .ThenBy(item => item.ModuleId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return definition != null;
    }

    static bool TryResolveDefinition(
        IEnumerable<GridModuleDefinition> definitions,
        GridModuleCategory category,
        string preferredId,
        out GridModuleDefinition definition,
        bool requireSingleCell = false)
    {
        definition = definitions
            .Where(item =>
                item != null &&
                item.Category == category &&
                (!requireSingleCell || CellCount(item.Footprint) == 1))
            .OrderBy(item => PreferredIdScore(item.ModuleId, preferredId))
            .ThenBy(item => CellCount(item.Footprint))
            .ThenBy(item => item.ModuleId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return definition != null;
    }

    static int PreferredIdScore(string moduleId, string preferredId) =>
        !string.IsNullOrWhiteSpace(moduleId) &&
        moduleId.IndexOf(
            preferredId,
            StringComparison.OrdinalIgnoreCase) >= 0
            ? 0
            : 1;

    static int CellCount(Vector3Int size) =>
        Mathf.Max(1, size.x * size.y * size.z);

    static bool IsCoreCell(int x, int y, int z) =>
        x >= -1 && x <= 0 &&
        y >= -1 && y <= 0 &&
        z >= -1 && z <= 0;

    static int DistanceFromCore(Vector3Int cell)
    {
        int dx = cell.x < -1 ? -1 - cell.x :
            cell.x > 0 ? cell.x : 0;
        int dy = cell.y < -1 ? -1 - cell.y :
            cell.y > 0 ? cell.y : 0;
        int dz = cell.z < -1 ? -1 - cell.z :
            cell.z > 0 ? cell.z : 0;
        return dx + dy + dz;
    }

    static List<Vector2Int> BuildFaceCoordinates(
        int radius,
        System.Random random)
    {
        int low = -radius;
        int high = radius - 1;
        var oppositePairs = new List<Vector2Int[]>();
        for (int first = low; first <= high; first++)
        for (int second = low; second <= high; second++)
        {
            int oppositeFirst = -1 - first;
            int oppositeSecond = -1 - second;
            if (first > oppositeFirst ||
                (first == oppositeFirst && second > oppositeSecond))
            {
                continue;
            }
            oppositePairs.Add(new[]
            {
                new Vector2Int(first, second),
                new Vector2Int(oppositeFirst, oppositeSecond)
            });
        }
        for (int index = oppositePairs.Count - 1; index > 0; index--)
        {
            int swap = random.Next(index + 1);
            Vector2Int[] value = oppositePairs[index];
            oppositePairs[index] = oppositePairs[swap];
            oppositePairs[swap] = value;
        }
        var result = new List<Vector2Int>(oppositePairs.Count * 2);
        foreach (Vector2Int[] pair in oppositePairs)
        {
            result.Add(pair[0]);
            result.Add(pair[1]);
        }
        return result;
    }

    static Vector3Int ResolveSurfaceCell(
        Vector3Int outwardNormal,
        Vector2Int coordinates,
        int radius)
    {
        int boundary = outwardNormal.x < 0 ||
                       outwardNormal.y < 0 ||
                       outwardNormal.z < 0
            ? -radius
            : radius - 1;
        if (outwardNormal.x != 0)
            return new Vector3Int(
                boundary,
                coordinates.x,
                coordinates.y);
        if (outwardNormal.y != 0)
            return new Vector3Int(
                coordinates.x,
                boundary,
                coordinates.y);
        return new Vector3Int(
            coordinates.x,
            coordinates.y,
            boundary);
    }

    static bool TryPlaceOnSurface(
        GridAssemblyModel model,
        GridModuleDefinition definition,
        Vector3Int surfaceCell,
        Vector3Int outwardNormal,
        out string runtimeId)
    {
        runtimeId = string.Empty;
        Vector3Int target = surfaceCell + outwardNormal;
        for (int quarterTurn = 0; quarterTurn < 4; quarterTurn++)
        {
            int orientation = GridOrientation.FromOutwardNormal(
                outwardNormal,
                quarterTurn);
            List<Vector3Int> cells = GridOrientation.NormalizedCells(
                definition.Footprint,
                orientation);
            foreach (Vector3Int anchor in cells)
            {
                GridModulePose pose = new GridModulePose(
                    target - anchor,
                    orientation);
                if (model.TryPlace(
                        definition.ModuleId,
                        pose,
                        false,
                        out runtimeId,
                        out _))
                    return true;
            }
        }
        return false;
    }

    static int PlaceWeapons(
        GridAssemblyModel model,
        GridModuleDefinition weapon,
        int radius,
        int requested,
        System.Random random)
    {
        int orientation = GridOrientation.FromForwardAndUp(
            Vector3.forward,
            Vector3.up);
        List<Vector2Int> pairedCoordinates =
            BuildFaceCoordinates(radius, random);
        int placed = 0;
        for (int index = 0;
             index + 1 < pairedCoordinates.Count && placed < requested;
             index += 2)
        {
            Vector2Int first = pairedCoordinates[index];
            Vector2Int second = pairedCoordinates[index + 1];
            if (!model.TryPlace(
                    weapon.ModuleId,
                    new GridModulePose(
                        new Vector3Int(first.x, radius, first.y),
                        orientation),
                    false,
                    out string firstId,
                    out _))
            {
                continue;
            }
            if (!model.TryPlace(
                    weapon.ModuleId,
                    new GridModulePose(
                        new Vector3Int(
                            second.x,
                            -radius - 1,
                            second.y),
                        orientation),
                    false,
                    out _,
                    out _))
            {
                model.TryRemove(firstId, out _);
                continue;
            }
            placed += 2;
        }
        return placed;
    }

    static ModularBossThrusterDirection ToDirection(Vector3Int direction)
    {
        if (direction == Vector3Int.right)
            return ModularBossThrusterDirection.Right;
        if (direction == Vector3Int.left)
            return ModularBossThrusterDirection.Left;
        if (direction == Vector3Int.up)
            return ModularBossThrusterDirection.Up;
        if (direction == Vector3Int.down)
            return ModularBossThrusterDirection.Down;
        if (direction == Vector3Int.forward)
            return ModularBossThrusterDirection.Forward;
        return ModularBossThrusterDirection.Backward;
    }
}

[DisallowMultipleComponent]
public sealed class ModularBossGridFlightSession :
    MonoBehaviour,
    IGridFlightSession
{
    public GridFlightState State { get; private set; } =
        GridFlightState.Flight;
    public bool IsFlying => State == GridFlightState.Flight;
    public event Action<GridFlightState, string> StateChanged;

    public void ExitFlight()
    {
        if (State == GridFlightState.Build)
            return;
        State = GridFlightState.Build;
        StateChanged?.Invoke(State, "Boss flight ended.");
    }
}

/// <summary>
/// Simple first-pass Boss brain. Steering is expressed as ordinary RC3 pilot
/// input, so surviving thrusters still determine the motion that is possible.
/// </summary>
[DisallowMultipleComponent]
public sealed class ModularBossCombatRuntime : MonoBehaviour
{
    const float EmergencyAssistThreshold = 0.58f;
    const float BridgeContactGraceSeconds = 0.16f;
    const float ObstacleProbeDistance = 42f;
    static readonly RaycastHit[] ObstacleProbeHits = new RaycastHit[24];
    static readonly RaycastHit[] SightProbeHits = new RaycastHit[32];
    static readonly RaycastHit[] PlayerCoverProbeHits = new RaycastHit[128];
    static readonly RaycastHit[] WeaponSightHits = new RaycastHit[64];
    static readonly Vector3[] CoverProbeDirections =
    {
        Vector3.right,
        Vector3.left,
        Vector3.forward,
        Vector3.back,
        Vector3.up,
        Vector3.down
    };
    static readonly ModularBossThrusterDirection[] DirectionValues =
        (ModularBossThrusterDirection[])Enum.GetValues(
            typeof(ModularBossThrusterDirection));

    readonly List<GridModuleView> liveWeapons =
        new List<GridModuleView>();
    readonly Dictionary<ModularBossThrusterDirection, int> liveThrusters =
        new Dictionary<ModularBossThrusterDirection, int>();
    readonly Collider[] spawnOverlapBuffer = new Collider[2048];
    readonly GridModuleView[] collisionModuleCandidates =
        new GridModuleView[3];
    readonly float[] collisionModuleCandidateDistances =
        new float[3];
    readonly UrbanDestructibleBuilding[] playerCoverCandidates =
        new UrbanDestructibleBuilding[24];
    readonly Collider[] playerCoverCandidateColliders = new Collider[24];
    readonly int[] playerCoverCandidateSampleMasks = new int[24];
    readonly float[] playerCoverCandidateDepths = new float[24];
    readonly List<Vector3> safeNavigationTrail =
        new List<Vector3>(
            ModularBossCombatPolicy.SafeNavigationTrailCapacity);

    Rigidbody body;
    Rigidbody playerBody;
    VehicleStructureGraph playerGraph;
    VehicleStructureGraph structureGraph;
    GridAssemblyPresenter presenter;
    RobocraftMotionCoordinator motion;
    ModularBossGridFlightSession flight;
    ModularBossReadabilityPresentation readability;
    ModularBossShieldRuntime shield;
    WeaponVisualPool visuals;
    ModularBossBuildResult build;
    float nextShotAt;
    float nextTargetSearchAt;
    int weaponCursor;
    VehicleModuleDamageReceiver focusedPlayerModule;
    Collider focusedPlayerCollider;
    bool prepared;
    bool emergencyAssist;
    bool combatActive;
    bool spawnWasAdjusted;
    float healthyActuatorForceMultiplier = 1f;
    Vector3 smoothedAimForward = Vector3.forward;
    ModularBossObstacleState obstacleState =
        ModularBossObstacleState.Pursuit;
    UrbanDestructibleBridge contactedBridge;
    UrbanDestructibleBridge lockedBridge;
    UrbanDestructibleBridge probedBlockingBridge;
    Vector3 lastBridgeContactPoint;
    Vector3 probedBridgePoint;
    Vector3 avoidanceDirection;
    Vector3 committedAvoidanceDirection;
    float avoidanceCommitEndsAt;
    Vector3 avoidanceProgressPosition;
    float avoidanceProgressStartedAt = -1f;
    Vector3 collisionEscapeDirection;
    float lastWorldCollisionAt = -1f;
    float bridgeContactStartedAt = -1f;
    float lastBridgeContactAt = -1f;
    float contactedBridgeScore = float.NegativeInfinity;
    float bridgeContactConfirmedAt = -1f;
    float obstacleStateStartedAt;
    float stateEndsAt;
    float immunityEndsAt;
    int ceaseFireWindowCount;
    bool forceBridgeApproach;
    bool directLineBlocked;
    bool nearbyUrbanCover;
    bool playerEntrenchedInCover;
    UrbanDestructibleBuilding blockingCoverBuilding;
    UrbanDestructibleRuinSection blockingCoverRuin;
    Collider blockingCoverCollider;
    UrbanDestructibleBuilding contactedBuilding;
    Collider contactedBuildingCollider;
    UrbanDestructibleBuilding stuckObservationBuilding;
    Vector3 stuckObservationPosition;
    float stuckObservationStartedAt = -1f;
    float nextAwarenessProbeAt;
    float coverObservedAt = -1f;
    float coveredSince = -1f;
    float nextPlayerRamAt;
    float nextCoverBreachAt;
    float nextPlayerImpactAt;
    float nextBossBuildingLossAt;
    float lastCommittedPlayerRamAt = -100f;
    Vector3 committedRamDirection;
    Vector3 committedRamTarget;
    UrbanDestructibleBuilding coverBreachBuilding;
    Collider coverBreachCollider;
    Vector3 coverBreachPoint;
    bool resumeBacktrackAfterCoverBreach;
    Vector3 recoveryEscapeDirection;
    float recoveryEscapeEndsAt;
    int backtrackTargetIndex = -1;
    Vector3 backtrackTarget;
    Vector3 backtrackProgressPosition;
    float backtrackProgressStartedAt = -1f;

    public event Action Destroyed;

    public bool IsPrepared => prepared;
    public string PreparationError { get; private set; } = string.Empty;
    public VehicleStructureGraph StructureGraph => structureGraph;
    public float IntegrityRatio => structureGraph == null
        ? 0f
        : structureGraph.OverallHealthRatio;
    public int LiveWeaponCount => liveWeapons.Count;
    public int LiveThrusterCount => liveThrusters.Values.Sum();
    public int InitialThrusterCount => build?.ThrusterDirections.Count ?? 0;
    public float ShieldIntegrityRatio => shield == null
        ? 0f
        : shield.IntegrityRatio;
    public bool ShieldActive => shield != null && shield.IsActive;
    public bool EmergencyAssistActive => emergencyAssist;
    public bool SpawnWasAdjusted => spawnWasAdjusted;
    public float HealthyActuatorForceMultiplier =>
        healthyActuatorForceMultiplier;
    public ModularBossObstacleState ObstacleState => obstacleState;
    public bool WeaponsSuppressed =>
        obstacleState == ModularBossObstacleState.BlockedCeaseFire ||
        obstacleState == ModularBossObstacleState.PlayerRamTelegraph ||
        obstacleState == ModularBossObstacleState.PlayerRamCharge ||
        obstacleState == ModularBossObstacleState.CoverBreachTelegraph ||
        obstacleState == ModularBossObstacleState.CoverBreachCharge ||
        obstacleState == ModularBossObstacleState.BacktrackEscape ||
        obstacleState == ModularBossObstacleState.Recovery ||
        (shield != null && shield.IsBreakWeaponStaggerActive);
    public float CeaseFireRemaining => obstacleState ==
        ModularBossObstacleState.BlockedCeaseFire
        ? Mathf.Max(0f, stateEndsAt - Time.time)
        : 0f;
    public float RamImmunityRemaining => Mathf.Max(
        0f,
        immunityEndsAt - Time.time);
    public int CeaseFireWindowCount => ceaseFireWindowCount;
    public string ObstacleStateLabel
    {
        get
        {
            switch (obstacleState)
            {
                case ModularBossObstacleState.LocalAvoidance:
                    return "Boss 正在绕开障碍";
                case ModularBossObstacleState.BlockedCeaseFire:
                    return $"Boss 受阻，武器停火 {CeaseFireRemaining:0.0} 秒";
                case ModularBossObstacleState.RamCharge:
                    return RamImmunityRemaining > 0f
                        ? $"Boss 正在撞毁连廊 · 停火免疫 {RamImmunityRemaining:0.0} 秒"
                        : "Boss 正在撞毁连廊";
                case ModularBossObstacleState.Recovery:
                    return "Boss 正在恢复姿态";
                case ModularBossObstacleState.PlayerRamTelegraph:
                    return "Boss 锁定冲撞：立即横向闪避";
                case ModularBossObstacleState.PlayerRamCharge:
                    return "Boss 已承诺冲撞方向";
                case ModularBossObstacleState.CoverBreachTelegraph:
                    return "Boss 正在锁定掩体";
                case ModularBossObstacleState.BacktrackEscape:
                    return "Boss 正在沿安全路径脱离";
                case ModularBossObstacleState.CoverBreachCharge:
                    return "Boss 正在撞毁掩体";
                default:
                    return RamImmunityRemaining > 0f
                        ? $"连廊停火免疫 {RamImmunityRemaining:0.0} 秒"
                        : string.Empty;
            }
        }
    }
    public string DamageStateLabel
    {
        get
        {
            if (structureGraph == null)
                return "Boss preparing";
            string shieldState = shield == null
                ? string.Empty
                : shield.StatusLabel + "\n";
            if (emergencyAssist)
                return shieldState +
                       "Emergency stabilization / reduced mobility";
            if (IntegrityRatio < 0.65f)
                return shieldState +
                       "Structure damaged / thrust asymmetric";
            return shieldState + "Systems operational";
        }
    }

    public IEnumerator Prepare(
        InfinitePlanarSurfaceWorld world,
        Rigidbody targetPlayerBody,
        VehicleStructureGraph targetPlayerGraph,
        GridAssemblyModel playerModel,
        ModularContentService contentService,
        IReadOnlyDictionary<string, ModularContentRecord> contentRecords,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        int planetTier,
        int seed)
    {
        prepared = false;
        PreparationError = string.Empty;
        ClearFocusedPlayerTarget();
        playerBody = targetPlayerBody;
        playerGraph = targetPlayerGraph;
        if (world == null || playerBody == null || playerGraph == null ||
            playerModel == null || contentService == null ||
            contentRecords == null)
        {
            PreparationError =
                "Boss runtime is missing the planet, player graph or module catalog.";
            yield break;
        }
        if (!ModularBossPcgGenerator.TryBuild(
                playerModel.Definitions.Values,
                planetTier,
                seed,
                out build,
                out string generationError))
        {
            PreparationError = generationError;
            yield break;
        }

        gameObject.name = "ModularGraphBoss";
        gameObject.AddComponent<PlanetFloatingOriginParticipant>();
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        body = gameObject.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.drag = 0f;
        body.angularDrag = 0f;
        body.isKinematic = true;
        body.collisionDetectionMode =
            CollisionDetectionMode.ContinuousSpeculative;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        Transform parts = new GameObject("Parts").transform;
        parts.SetParent(transform, false);
        Transform core = new GameObject("CoreVisual").transform;
        core.SetParent(transform, false);
        ShipAssembly assembly = gameObject.AddComponent<ShipAssembly>();
        assembly.Configure(body, parts, null, 650f);
        presenter = gameObject.AddComponent<GridAssemblyPresenter>();
        presenter.Initialize(build.Model, assembly, core);
        ApplyBossModuleScale();

        NeoXCatalogIntegration integration =
            gameObject.AddComponent<NeoXCatalogIntegration>();
        integration.InitializeRuntime(
            contentService,
            presenter,
            contentRecords);
        while (!integration.IsReady || contentService.IsLoadingAssets)
            yield return null;
        integration.StopRuntimeRebuildTracking();
        presenter.SetRuntimeRemovalOptimization(true);

        flight = gameObject.AddComponent<ModularBossGridFlightSession>();
        structureGraph = gameObject.AddComponent<VehicleStructureGraph>();
        structureGraph.Initialize(build.Model, presenter, flight);
        structureGraph.SetAutomaticReturnToBuild(false);
        structureGraph.ConfigureIntegrityMultipliers(
            build.Profile.StructureIntegrityMultiplier,
            build.Profile.CoreIntegrityMultiplier,
            build.Profile.SystemIntegrityMultiplier);
        structureGraph.SetDamageEnabled(true);
        structureGraph.BeginFlight();
        EnsureSafeSpawn(spawnPosition, spawnRotation);

        PlanetEnvironmentProvider environment =
            gameObject.AddComponent<PlanetEnvironmentProvider>();
        PlanetCelestialProfile celestial =
            world.Definition != null && world.Definition.celestial != null
                ? world.Definition.celestial
                : PlanetCelestialProfile.CreateCompatibleDefault();
        environment.Configure(world, celestial.Physical);

        motion = gameObject.AddComponent<RobocraftMotionCoordinator>();
        motion.ConfigureExplicit(body, assembly, build.Model, presenter);
        motion.SetMassGeometryScale(
            ModularBossModuleScalePolicy.LinearScale);
        motion.SetEnvironmentProvider(environment);
        motion.ControlsEnabled = true;
        motion.SetCoreAssistMode(VehicleCoreAssistMode.Standard);
        float gravityMagnitude = world.GetGravity(transform.position).magnitude;
        healthyActuatorForceMultiplier =
            ModularBossFlightAuthorityPolicy.ResolveMultiplier(
                motion.Telemetry.totalMass,
                gravityMagnitude,
                motion.CaptureActuatorDiagnostics()) *
            ModularBossCombatPolicy.PursuitForceMultiplier;
        healthyActuatorForceMultiplier = Mathf.Min(
            ModularBossFlightAuthorityPolicy.MaximumMultiplier,
            healthyActuatorForceMultiplier);
        motion.SetActuatorForceMultiplier(
            healthyActuatorForceMultiplier);
        Vector3 emergencyCoreForce =
            ModularBossFlightAuthorityPolicy.ResolveEmergencyCoreForce(
                motion.Telemetry.totalMass,
                gravityMagnitude);
        motion.ConfigureTrainingCoreAuthority(
            emergencyCoreForce.y,
            ModularBossFlightAuthorityPolicy.ResolveEmergencyDownForce(
                motion.Telemetry.totalMass),
            Mathf.Max(emergencyCoreForce.x, emergencyCoreForce.z));
        if (!ModularBossFlightAuthorityPolicy.HasMinimumAuthority(
                motion.Telemetry.totalMass,
                gravityMagnitude,
                motion.CaptureActuatorDiagnostics()))
        {
            PreparationError =
                "Boss generation did not produce enough six-axis thrust authority.";
            yield break;
        }
        if (!motion.TryBeginFlight(false, out string physicsMessage))
        {
            PreparationError = "Boss RC3 startup failed: " + physicsMessage;
            yield break;
        }
        body.maxAngularVelocity =
            ModularBossCombatPolicy.MaximumStableAngularSpeed;
        smoothedAimForward = transform.forward;

        visuals = gameObject.AddComponent<WeaponVisualPool>();
        visuals.Prewarm();
        VehicleCombatTeamUtility.SetTeam(
            gameObject,
            VehicleCombatTeam.Enemy);
        structureGraph.StructureChanged += HandleStructureChanged;
        structureGraph.Destroyed += HandleDestroyed;
        RebuildLiveModules();
        readability = gameObject.AddComponent<
            ModularBossReadabilityPresentation>();
        readability.Configure(presenter, build);
        readability.StopTrackingPresenterRebuilds();
        shield = gameObject.AddComponent<ModularBossShieldRuntime>();
        shield.Configure(structureGraph, presenter, build.Profile.Tier);
        structureGraph.SetModuleDamageFilter(
            shield.FilterIncomingDamage);
        prepared = true;
    }

    public void SetCombatActive(bool value)
    {
        combatActive = value && prepared &&
                       structureGraph != null &&
                       !structureGraph.IsVehicleDestroyed;
        shield?.SetCombatActive(combatActive);
        if (body == null)
            return;
        if (combatActive && body.isKinematic)
            EnsureSafeSpawn(transform.position, transform.rotation);
        if (combatActive)
        {
            body.isKinematic = false;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        else
        {
            // Unity rejects velocity writes after a body becomes kinematic.
            // Stop the dynamic body first, then freeze it for the inactive
            // mission state.
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
        }
        if (!combatActive)
        {
            motion?.SetInjectedControl(default);
            ResetObstacleRuntime();
        }
        else
        {
            smoothedAimForward = transform.forward;
            nextTargetSearchAt = 0f;
            nextPlayerRamAt = Time.time + 1.4f;
            nextCoverBreachAt = Time.time + 2.5f;
            ResetSafeNavigationTrail();
            SetObstacleState(ModularBossObstacleState.Pursuit);
            TryRecordSafeNavigationPoint(true);
        }
    }

    void Update()
    {
        if (!RuntimeDependenciesAvailable() ||
            !playerGraph.Active || structureGraph == null ||
            !structureGraph.Active)
            return;
        UpdateEmergencyAssist();
        TryFire();
    }

    void FixedUpdate()
    {
        if (!RuntimeDependenciesAvailable() ||
            !playerGraph.Active || !structureGraph.Active)
        {
            return;
        }
        UpdateCombatAwareness();
        TryRecordSafeNavigationPoint(false);
        UpdateObstacleState();
        UpdatePilotControl();
    }

    bool RuntimeDependenciesAvailable()
    {
        return combatActive && prepared &&
               body != null && motion != null && build != null &&
               playerBody != null && playerGraph != null &&
               structureGraph != null;
    }

    void UpdateEmergencyAssist()
    {
        float ratio = InitialThrusterCount <= 0
            ? 0f
            : LiveThrusterCount / (float)InitialThrusterCount;
        bool criticalDirection = false;
        foreach (ModularBossThrusterDirection direction in DirectionValues)
        {
            int initial = InitialThrustersFor(direction);
            int live = LiveThrustersFor(direction);
            if (initial <= 0 ||
                live > Mathf.FloorToInt(initial * 0.25f))
                continue;
            criticalDirection = true;
            break;
        }
        bool shouldAssist = ratio <= EmergencyAssistThreshold ||
                            criticalDirection;
        if (shouldAssist == emergencyAssist)
            return;
        emergencyAssist = shouldAssist;
        motion.SetCoreAssistMode(
            shouldAssist
                ? VehicleCoreAssistMode.Training
                : VehicleCoreAssistMode.Standard);
    }

    void UpdateCombatAwareness()
    {
        float now = Time.time;
        if (now < nextAwarenessProbeAt || playerBody == null ||
            structureGraph == null)
        {
            return;
        }
        nextAwarenessProbeAt = now + 0.12f;
        directLineBlocked = false;
        nearbyUrbanCover = false;
        blockingCoverBuilding = null;
        blockingCoverRuin = null;
        blockingCoverCollider = null;

        Bounds bossBounds = structureGraph.ResolveVisualBounds();
        Vector3 origin = bossBounds.center;
        Vector3 target = playerBody.worldCenterOfMass;
        Vector3 ray = target - origin;
        float rayDistance = ray.magnitude;
        float nearest = rayDistance;
        if (rayDistance > 0.01f)
        {
            int count = Physics.RaycastNonAlloc(
                origin,
                ray / rayDistance,
                SightProbeHits,
                rayDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider collider = SightProbeHits[index].collider;
                if (!IsRelevantWorldCollider(collider) ||
                    SightProbeHits[index].distance >= nearest)
                {
                    continue;
                }
                nearest = SightProbeHits[index].distance;
                directLineBlocked = true;
                blockingCoverCollider = collider;
                blockingCoverBuilding = ResolveUrbanBuilding(collider);
                blockingCoverRuin = ResolveUrbanRuin(collider);
            }
        }

        Bounds playerBounds = playerGraph.ResolveVisualBounds();
        if (TrySelectPlayerCoverBuilding(
                origin,
                playerBounds,
                out UrbanDestructibleBuilding selectedCoverBuilding,
                out Collider selectedCoverCollider))
        {
            directLineBlocked = true;
            blockingCoverBuilding = selectedCoverBuilding;
            blockingCoverCollider = selectedCoverCollider;
            blockingCoverRuin = null;
        }

        if (TryFindNearbyUrbanCover(
                target,
                out _,
                out _,
                out _))
        {
            nearbyUrbanCover = true;
        }

        playerEntrenchedInCover =
            ModularBossCombatPolicy.IsPlayerEntrenched(
                HasIntactBuildingCover(target, Vector3.right),
                HasIntactBuildingCover(target, Vector3.left),
                HasIntactBuildingCover(target, Vector3.forward),
                HasIntactBuildingCover(target, Vector3.back));

    }

    bool TrySelectPlayerCoverBuilding(
        Vector3 bossCenter,
        Bounds playerBounds,
        out UrbanDestructibleBuilding selectedBuilding,
        out Collider selectedCollider)
    {
        selectedBuilding = null;
        selectedCollider = null;
        Bounds bossBounds = structureGraph.ResolveVisualBounds();
        int candidateCount = 0;
        for (int sampleIndex = 0;
             sampleIndex < ModularBossCombatPolicy.PlayerCoverSampleCount;
             sampleIndex++)
        {
            Vector3 sample = ResolvePlayerCoverSample(
                playerBounds,
                sampleIndex);
            Vector3 centerRay = sample - bossCenter;
            float centerDistance = centerRay.magnitude;
            if (centerDistance <= 0.01f)
                continue;
            Vector3 direction = centerRay / centerDistance;
            float bossSurfaceOffset =
                Mathf.Abs(direction.x) * bossBounds.extents.x +
                Mathf.Abs(direction.y) * bossBounds.extents.y +
                Mathf.Abs(direction.z) * bossBounds.extents.z + 0.5f;
            bossSurfaceOffset = Mathf.Min(
                bossSurfaceOffset,
                centerDistance * 0.45f);
            Vector3 rayOrigin = bossCenter + direction * bossSurfaceOffset;
            Vector3 ray = sample - rayOrigin;
            float rayDistance = ray.magnitude;
            if (rayDistance <= 0.01f)
                continue;

            int hitCount = Physics.RaycastNonAlloc(
                rayOrigin,
                ray / rayDistance,
                PlayerCoverProbeHits,
                rayDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                RaycastHit hit = PlayerCoverProbeHits[hitIndex];
                Collider collider = hit.collider;
                if (!IsRelevantWorldCollider(collider))
                    continue;
                UrbanDestructibleBuilding building =
                    ResolveUrbanBuilding(collider);
                if (building == null || building.IsUrbanDestroyed)
                    continue;

                int candidateIndex = -1;
                for (int index = 0; index < candidateCount; index++)
                {
                    if (playerCoverCandidates[index] != building)
                        continue;
                    candidateIndex = index;
                    break;
                }
                if (candidateIndex < 0)
                {
                    if (candidateCount >= playerCoverCandidates.Length)
                        continue;
                    candidateIndex = candidateCount++;
                    playerCoverCandidates[candidateIndex] = building;
                    playerCoverCandidateColliders[candidateIndex] = collider;
                    playerCoverCandidateSampleMasks[candidateIndex] = 0;
                    playerCoverCandidateDepths[candidateIndex] = 0f;
                }

                int sampleBit = 1 << sampleIndex;
                if ((playerCoverCandidateSampleMasks[candidateIndex] &
                     sampleBit) == 0)
                {
                    playerCoverCandidateSampleMasks[candidateIndex] |=
                        sampleBit;
                }
                float normalizedDepth = hit.distance / rayDistance;
                if (normalizedDepth >
                    playerCoverCandidateDepths[candidateIndex])
                {
                    playerCoverCandidateDepths[candidateIndex] =
                        normalizedDepth;
                    playerCoverCandidateColliders[candidateIndex] = collider;
                }
            }
        }

        float bestScore = float.NegativeInfinity;
        for (int index = 0; index < candidateCount; index++)
        {
            int sampleMask = playerCoverCandidateSampleMasks[index];
            // The center sample is the actual Boss-to-player firing corridor.
            // Side-only buildings are context, not demolition targets.
            if ((sampleMask & 1) == 0)
                continue;
            UrbanDestructibleBuilding building = playerCoverCandidates[index];
            if (building == null || building.IsUrbanDestroyed)
                continue;
            int coveredSamples = CountSetBits(sampleMask);
            float distanceFromPlayer = Vector3.Distance(
                playerBounds.center,
                building.DestructionBounds.ClosestPoint(
                    playerBounds.center));
            float score = ModularBossCombatPolicy.ScorePlayerCoverBuilding(
                coveredSamples,
                distanceFromPlayer,
                playerCoverCandidateDepths[index]);
            if (score <= bestScore)
                continue;
            bestScore = score;
            selectedBuilding = building;
            selectedCollider = playerCoverCandidateColliders[index];
        }

        for (int index = 0; index < candidateCount; index++)
        {
            playerCoverCandidates[index] = null;
            playerCoverCandidateColliders[index] = null;
            playerCoverCandidateSampleMasks[index] = 0;
            playerCoverCandidateDepths[index] = 0f;
        }
        return selectedBuilding != null;
    }

    static Vector3 ResolvePlayerCoverSample(Bounds bounds, int sampleIndex)
    {
        switch (sampleIndex)
        {
            case 1:
                return bounds.center + Vector3.right * bounds.extents.x * 0.7f;
            case 2:
                return bounds.center - Vector3.right * bounds.extents.x * 0.7f;
            case 3:
                return bounds.center + Vector3.up * bounds.extents.y * 0.7f;
            case 4:
                return bounds.center - Vector3.up * bounds.extents.y * 0.7f;
            case 5:
                return bounds.center + Vector3.forward * bounds.extents.z * 0.7f;
            case 6:
                return bounds.center - Vector3.forward * bounds.extents.z * 0.7f;
            default:
                return bounds.center;
        }
    }

    static int CountSetBits(int value)
    {
        int count = 0;
        while (value != 0)
        {
            value &= value - 1;
            count++;
        }
        return count;
    }

    bool HasIntactBuildingCover(Vector3 origin, Vector3 direction)
    {
        int count = Physics.RaycastNonAlloc(
            origin,
            direction,
            SightProbeHits,
            ModularBossCombatPolicy.EntrenchedCoverRadius,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        for (int index = 0; index < count; index++)
        {
            Collider collider = SightProbeHits[index].collider;
            if (!IsRelevantWorldCollider(collider))
                continue;
            UrbanDestructibleBuilding building =
                ResolveUrbanBuilding(collider);
            if (building != null && !building.IsUrbanDestroyed)
                return true;
        }
        return false;
    }

    bool TryFindNearbyUrbanCover(
        Vector3 origin,
        out UrbanDestructibleBuilding nearestBuilding,
        out UrbanDestructibleRuinSection nearestRuin,
        out Collider nearestCollider)
    {
        nearestBuilding = null;
        nearestRuin = null;
        nearestCollider = null;
        float nearestDistance = float.PositiveInfinity;
        bool found = false;
        for (int directionIndex = 0;
             directionIndex < CoverProbeDirections.Length;
             directionIndex++)
        {
            int count = Physics.RaycastNonAlloc(
                origin,
                CoverProbeDirections[directionIndex],
                SightProbeHits,
                ModularBossCombatPolicy.NearbyCoverRadius,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (int hitIndex = 0; hitIndex < count; hitIndex++)
            {
                Collider collider = SightProbeHits[hitIndex].collider;
                if (!IsRelevantWorldCollider(collider) ||
                    !IsUrbanCover(collider))
                {
                    continue;
                }
                found = true;
                UrbanDestructibleBuilding building =
                    ResolveUrbanBuilding(collider);
                UrbanDestructibleRuinSection ruin =
                    ResolveUrbanRuin(collider);
                bool validBuilding = building != null &&
                                     !building.IsUrbanDestroyed;
                bool validRuin = ruin != null && !ruin.IsUrbanDestroyed;
                if ((!validBuilding && !validRuin) ||
                    SightProbeHits[hitIndex].distance >= nearestDistance)
                {
                    continue;
                }
                nearestDistance = SightProbeHits[hitIndex].distance;
                nearestBuilding = validBuilding ? building : null;
                nearestRuin = validRuin ? ruin : null;
                nearestCollider = collider;
            }
        }
        return found;
    }

    bool IsRelevantWorldCollider(Collider collider)
    {
        return collider != null && collider.enabled &&
               !collider.transform.IsChildOf(transform) &&
               (playerBody == null ||
                !collider.transform.IsChildOf(playerBody.transform));
    }

    static bool IsUrbanCover(Collider collider)
    {
        if (collider == null)
            return false;
        UrbanDestructibleBridge bridge =
            collider.GetComponentInParent<UrbanDestructibleBridge>();
        if (bridge != null)
            return !bridge.IsUrbanDestroyed;
        UrbanDestructibleRuinSection ruin =
            collider.GetComponentInParent<UrbanDestructibleRuinSection>();
        if (ruin != null)
            return !ruin.IsUrbanDestroyed;
        UrbanDestructibleBuilding building = ResolveUrbanBuilding(collider);
        return building != null && !building.IsUrbanDestroyed;
    }

    static UrbanDestructibleBuilding ResolveUrbanBuilding(Collider collider)
    {
        if (collider == null)
            return null;
        UrbanDestructibleBuilding building =
            collider.GetComponentInParent<UrbanDestructibleBuilding>();
        if (building != null)
            return building;
        UrbanStructuralColliderProxy proxy =
            collider.GetComponentInParent<UrbanStructuralColliderProxy>();
        return proxy != null ? proxy.Owner : null;
    }

    static UrbanDestructibleRuinSection ResolveUrbanRuin(Collider collider)
    {
        return collider != null
            ? collider.GetComponentInParent<UrbanDestructibleRuinSection>()
            : null;
    }

    void UpdateObstacleState()
    {
        float now = Time.time;
        if (lockedBridge != null && lockedBridge.IsUrbanDestroyed)
        {
            CompleteInterruptedBridgeWindow(now);
            ReleaseBridgeObstacleTracking();
            // A destroyed bridge used to remain locked until Recovery ended.
            // That made this branch restart the timer every physics frame, so
            // the Boss could never return to Pursuit.  Releasing the source
            // first makes one bridge destruction a one-shot transition.
            if (obstacleState != ModularBossObstacleState.Recovery)
                BeginRecovery(now);
            return;
        }

        if (obstacleState == ModularBossObstacleState.PlayerRamTelegraph)
        {
            if (now >= stateEndsAt)
            {
                SetObstacleState(ModularBossObstacleState.PlayerRamCharge);
                stateEndsAt = now +
                    ModularBossCombatPolicy.PlayerRamChargeSeconds;
                lastCommittedPlayerRamAt = now;
            }
            return;
        }
        if (obstacleState == ModularBossObstacleState.PlayerRamCharge)
        {
            if (now >= stateEndsAt)
                BeginRecovery(
                    now,
                    ModularBossCombatPolicy.PlayerRamRecoverySeconds);
            return;
        }
        if (obstacleState == ModularBossObstacleState.CoverBreachTelegraph)
        {
            if (!HasLiveCoverBreachTarget())
            {
                CompleteCoverBreachAttempt(now);
            }
            else if (now >= stateEndsAt)
            {
                SetObstacleState(ModularBossObstacleState.CoverBreachCharge);
                stateEndsAt = now +
                    ModularBossCombatPolicy.CoverBreachChargeSeconds;
            }
            return;
        }
        if (obstacleState == ModularBossObstacleState.CoverBreachCharge)
        {
            if (!HasLiveCoverBreachTarget() || now >= stateEndsAt)
            {
                CompleteCoverBreachAttempt(now);
            }
            return;
        }
        if (obstacleState == ModularBossObstacleState.BacktrackEscape)
        {
            UpdateBacktrackEscape(now);
            return;
        }

        bool recentBridgeContact = contactedBridge != null &&
            !contactedBridge.IsUrbanDestroyed &&
            now - lastBridgeContactAt <= BridgeContactGraceSeconds &&
            IsBridgeBlockingPursuit(contactedBridge);
        bool localObstacle = TryResolveLocalAvoidance(
            out Vector3 localAvoidance,
            out UrbanDestructibleBridge blockingBridge,
            out Vector3 blockingBridgePoint);
        if (blockingBridge != null &&
            !IsBridgeBlockingPursuit(blockingBridge))
        {
            blockingBridge = null;
        }
        Vector3 sightlineDirection = Vector3.zero;
        bool sightlineDetour = !playerEntrenchedInCover &&
            directLineBlocked &&
            TryResolveSightlineDetour(out sightlineDirection);
        if (sightlineDetour)
            localAvoidance = sightlineDirection;
        bool recentWorldCollision =
            now - lastWorldCollisionAt <= 0.32f &&
            collisionEscapeDirection.sqrMagnitude > 0.001f;
        if (recentWorldCollision)
        {
            localObstacle = true;
            sightlineDetour = false;
            localAvoidance = collisionEscapeDirection;
        }
        bool needsAvoidance = localObstacle || sightlineDetour;
        probedBlockingBridge = blockingBridge;
        probedBridgePoint = blockingBridgePoint;
        forceBridgeApproach = false;
        if (TryBeginStuckBuildingBreach(now, blockingBridge))
            return;
        if (TryBeginStalledAvoidanceRecovery(
                now,
                needsAvoidance,
                blockingBridge))
        {
            return;
        }
        if (needsAvoidance)
        {
            if (recentWorldCollision)
            {
                committedAvoidanceDirection = localAvoidance;
                avoidanceCommitEndsAt = now + 0.35f;
                avoidanceDirection = localAvoidance;
            }
            else
            {
                avoidanceDirection = CommitAvoidanceDirection(
                    localAvoidance,
                    now);
            }
        }

        // A wider city no longer guarantees opposing buildings within the
        // short entrenched probe. Persistent, intact cover on the actual
        // Boss-to-player firing corridor is sufficient authorization to
        // breach; the delay still gives a passing player time to move on.
        bool hardCoverBlocksBoss = directLineBlocked &&
            blockingCoverBuilding != null &&
            !blockingCoverBuilding.IsUrbanDestroyed;
        if (hardCoverBlocksBoss)
        {
            coverObservedAt = now;
            if (coveredSince < 0f)
                coveredSince = now;
        }
        else if (now - coverObservedAt >
                 ModularBossCombatPolicy.CoverMemorySeconds)
        {
            coveredSince = -1f;
        }

        if ((obstacleState == ModularBossObstacleState.Pursuit ||
             obstacleState == ModularBossObstacleState.LocalAvoidance) &&
            blockingBridge == null && lockedBridge == null)
        {
            bool coverPersisted = coveredSince >= 0f &&
                now - coveredSince >=
                ModularBossCombatPolicy.CoverBreachDelay(
                    build.Profile.Tier);
            if (now >= nextCoverBreachAt &&
                ModularBossCombatPolicy.ShouldAuthorizeCoverBreach(
                    directLineBlocked && HasLiveBlockingCoverTarget(),
                    coverPersisted))
            {
                BeginCoverBreach(
                    now,
                    blockingCoverBuilding,
                    blockingCoverCollider);
                return;
            }

            float playerDistance = Vector3.Distance(
                playerBody.worldCenterOfMass,
                body.worldCenterOfMass);
            if (obstacleState == ModularBossObstacleState.Pursuit &&
                !localObstacle && !directLineBlocked &&
                now >= nextPlayerRamAt &&
                playerDistance <= ModularBossCombatPolicy.PlayerRamDistance(
                    build.Profile.Tier))
            {
                BeginPlayerRam(now);
                return;
            }
        }

        switch (obstacleState)
        {
            case ModularBossObstacleState.Pursuit:
                if (recentBridgeContact)
                {
                    lockedBridge = contactedBridge;
                    bridgeContactConfirmedAt = -1f;
                    SetObstacleState(
                        ModularBossObstacleState.LocalAvoidance);
                }
                else if (needsAvoidance)
                {
                    lockedBridge = null;
                    SetObstacleState(
                        ModularBossObstacleState.LocalAvoidance);
                }
                break;

            case ModularBossObstacleState.LocalAvoidance:
            {
                if (recentBridgeContact)
                {
                    if (lockedBridge != contactedBridge)
                    {
                        lockedBridge = contactedBridge;
                        bridgeContactStartedAt = now;
                        bridgeContactConfirmedAt = -1f;
                    }
                    if (bridgeContactConfirmedAt < 0f &&
                        ModularBossObstaclePolicy.ShouldConfirmBridgeContact(
                            bridgeContactStartedAt,
                            now))
                    {
                        bridgeContactConfirmedAt = now;
                    }
                }

                bool sameBridgeStillBlocking = lockedBridge != null &&
                    ((recentBridgeContact &&
                      contactedBridge == lockedBridge) ||
                     blockingBridge == lockedBridge);
                if (bridgeContactConfirmedAt >= 0f)
                {
                    if (ModularBossObstaclePolicy.ShouldCommitRam(
                            bridgeContactConfirmedAt,
                            now,
                            sameBridgeStillBlocking))
                    {
                        if (now < immunityEndsAt)
                            BeginRam(now);
                        else
                            BeginCeaseFire(now);
                    }
                    else if (!sameBridgeStillBlocking &&
                             now - bridgeContactConfirmedAt >
                             BridgeContactGraceSeconds)
                    {
                        lockedBridge = null;
                        bridgeContactStartedAt = -1f;
                        bridgeContactConfirmedAt = -1f;
                    }
                }
                else if (ModularBossObstaclePolicy.
                         ShouldForceProbedBridgeApproach(
                             obstacleStateStartedAt,
                             now,
                             recentBridgeContact,
                             blockingBridge != null))
                {
                    // The local detour did not find clearance. Approach the
                    // probed bridge deliberately so real collision contact can
                    // confirm the ram sequence instead of hovering forever.
                    forceBridgeApproach = true;
                    avoidanceDirection = SafeDirection(
                        blockingBridgePoint -
                        structureGraph.ResolveVisualBounds().center,
                        localAvoidance);
                }
                else if (!recentBridgeContact &&
                         lockedBridge != null &&
                         now - lastBridgeContactAt >
                         BridgeContactGraceSeconds)
                {
                    lockedBridge = null;
                    bridgeContactStartedAt = -1f;
                    bridgeContactConfirmedAt = -1f;
                }

                if (!recentBridgeContact && !needsAvoidance &&
                    now - obstacleStateStartedAt > 0.2f)
                {
                    SetObstacleState(ModularBossObstacleState.Pursuit);
                }
                break;
            }

            case ModularBossObstacleState.BlockedCeaseFire:
                if (now >= stateEndsAt)
                {
                    ConsumeCeaseFireWindow(now);
                    BeginRam(now);
                }
                break;

            case ModularBossObstacleState.RamCharge:
                if (now < stateEndsAt)
                    break;
                if (lockedBridge != null &&
                    !lockedBridge.IsUrbanDestroyed)
                {
                    Vector3 ramDirection = body.velocity.sqrMagnitude > 1f
                        ? body.velocity.normalized
                        : transform.forward;
                    lockedBridge.TryBreakFromBossRam(
                        lastBridgeContactPoint,
                        ramDirection);
                }
                BeginRecovery(now);
                break;

            case ModularBossObstacleState.Recovery:
                if (now >= stateEndsAt)
                {
                    lockedBridge = null;
                    coverBreachBuilding = null;
                    coverBreachCollider = null;
                    recoveryEscapeDirection = Vector3.zero;
                    recoveryEscapeEndsAt = -1f;
                    SetObstacleState(ModularBossObstacleState.Pursuit);
                }
                break;
        }
    }

    void BeginCeaseFire(float now)
    {
        SetObstacleState(ModularBossObstacleState.BlockedCeaseFire);
        stateEndsAt = now +
            ModularBossObstaclePolicy.ResolveCeaseFireSeconds(
                build.Profile.Tier,
                ceaseFireWindowCount);
    }

    void ConsumeCeaseFireWindow(float now)
    {
        ceaseFireWindowCount++;
        immunityEndsAt = now +
                         ModularBossObstaclePolicy.RamImmunitySeconds;
    }

    void CompleteInterruptedBridgeWindow(float now)
    {
        if (obstacleState ==
            ModularBossObstacleState.BlockedCeaseFire)
        {
            ConsumeCeaseFireWindow(now);
        }
    }

    void BeginRam(float now)
    {
        SetObstacleState(ModularBossObstacleState.RamCharge);
        stateEndsAt = now + ModularBossObstaclePolicy.RamChargeSeconds;
    }

    void BeginPlayerRam(float now)
    {
        Vector3 toPlayer = playerBody.worldCenterOfMass -
                           body.worldCenterOfMass;
        float prediction = Mathf.Clamp(toPlayer.magnitude / 120f, 0.28f, 0.72f);
        committedRamTarget = playerBody.worldCenterOfMass +
                             playerBody.velocity * prediction;
        committedRamDirection = SafeDirection(
            committedRamTarget - body.worldCenterOfMass,
            transform.forward);
        SetObstacleState(ModularBossObstacleState.PlayerRamTelegraph);
        stateEndsAt = now +
                      ModularBossCombatPolicy.PlayerRamTelegraphSeconds;
        nextPlayerRamAt = now +
                          ModularBossCombatPolicy.PlayerRamCooldownSeconds;
    }

    void BeginCoverBreach(
        float now,
        UrbanDestructibleBuilding building,
        Collider collider,
        bool resumeBacktrack = false)
    {
        if (building == null || building.IsUrbanDestroyed)
            return;
        ResetStuckBuildingObservation();
        coverBreachBuilding = building;
        coverBreachCollider = collider;
        resumeBacktrackAfterCoverBreach = resumeBacktrack;
        Bounds targetBounds = building.DestructionBounds;
        coverBreachPoint = targetBounds.ClosestPoint(
            body.worldCenterOfMass);
        SetObstacleState(ModularBossObstacleState.CoverBreachTelegraph);
        stateEndsAt = now +
                      ModularBossCombatPolicy.CoverBreachTelegraphSeconds;
        nextCoverBreachAt = now +
                            ModularBossCombatPolicy.CoverBreachCooldownSeconds;
        coveredSince = -1f;
    }

    bool TryBeginStuckBuildingBreach(
        float now,
        UrbanDestructibleBridge blockingBridge)
    {
        bool canBreachFromCurrentState =
            obstacleState == ModularBossObstacleState.Pursuit ||
            obstacleState == ModularBossObstacleState.LocalAvoidance;
        bool liveBuildingContact =
            now - lastWorldCollisionAt <= 0.32f &&
            contactedBuilding != null &&
            !contactedBuilding.IsUrbanDestroyed &&
            contactedBuildingCollider != null;
        if (!canBreachFromCurrentState || !liveBuildingContact ||
            blockingBridge != null || lockedBridge != null || body == null)
        {
            ResetStuckBuildingObservation();
            return false;
        }

        Vector3 currentPosition = body.worldCenterOfMass;
        if (stuckObservationBuilding != contactedBuilding ||
            stuckObservationStartedAt < 0f)
        {
            stuckObservationBuilding = contactedBuilding;
            stuckObservationPosition = currentPosition;
            stuckObservationStartedAt = now;
            return false;
        }

        float progress = Vector3.Distance(
            stuckObservationPosition,
            currentPosition);
        if (progress >
            ModularBossCombatPolicy.BuildingStuckProgressDistance)
        {
            stuckObservationPosition = currentPosition;
            stuckObservationStartedAt = now;
            return false;
        }
        if (now - stuckObservationStartedAt <
            ModularBossCombatPolicy.BuildingStuckBreachSeconds)
        {
            return false;
        }

        UrbanDestructibleBuilding target = contactedBuilding;
        Collider targetCollider = contactedBuildingCollider;
        ResetStuckBuildingObservation();
        BeginCoverBreach(now, target, targetCollider, true);
        return obstacleState ==
               ModularBossObstacleState.CoverBreachTelegraph;
    }

    void ResetStuckBuildingObservation()
    {
        stuckObservationBuilding = null;
        stuckObservationPosition = Vector3.zero;
        stuckObservationStartedAt = -1f;
    }

    bool TryBeginStalledAvoidanceRecovery(
        float now,
        bool needsAvoidance,
        UrbanDestructibleBridge blockingBridge)
    {
        if (obstacleState != ModularBossObstacleState.LocalAvoidance ||
            !needsAvoidance || body == null || structureGraph == null ||
            blockingBridge != null || lockedBridge != null)
        {
            ResetAvoidanceProgressObservation();
            return false;
        }

        Vector3 current = body.worldCenterOfMass;
        if (avoidanceProgressStartedAt < 0f)
        {
            avoidanceProgressPosition = current;
            avoidanceProgressStartedAt = now;
            return false;
        }
        if (Vector3.Distance(current, avoidanceProgressPosition) >=
            ModularBossCombatPolicy.BuildingStuckProgressDistance)
        {
            avoidanceProgressPosition = current;
            avoidanceProgressStartedAt = now;
        }

        bool noTranslation = now - avoidanceProgressStartedAt >=
                             ModularBossCombatPolicy.
                                 BuildingStuckBreachSeconds;
        bool avoidanceLoop = now - obstacleStateStartedAt >=
                             ModularBossCombatPolicy.
                                 AvoidanceMaximumSeconds;
        if (!noTranslation && !avoidanceLoop)
            return false;

        // A real intact-building contact gets first refusal so it can be
        // breached instead of escaped. This branch handles probe-boundary
        // hover locks and ruin/city pockets that produce no live building.
        bool liveBuildingContact =
            now - lastWorldCollisionAt <= 0.55f &&
            contactedBuilding != null &&
            !contactedBuilding.IsUrbanDestroyed &&
            contactedBuildingCollider != null;
        if (liveBuildingContact)
            return false;

        ResetAvoidanceProgressObservation();
        if (!TryResolveVerticalEscapeDirection(out Vector3 escapeDirection))
            return TryBeginBacktrackEscape(now);

        recoveryEscapeDirection = escapeDirection;
        recoveryEscapeEndsAt = now +
            ModularBossCombatPolicy.VerticalEscapeSeconds;
        SetObstacleState(ModularBossObstacleState.Recovery);
        stateEndsAt = recoveryEscapeEndsAt;
        return true;
    }

    bool TryResolveVerticalEscapeDirection(out Vector3 direction)
    {
        direction = Vector3.zero;
        if (structureGraph == null)
            return false;

        Bounds bounds = structureGraph.ResolveVisualBounds();
        Vector3 planarToPlayer = playerBody == null
            ? transform.forward
            : Vector3.ProjectOnPlane(
                playerBody.worldCenterOfMass - bounds.center,
                Vector3.up);
        planarToPlayer = SafeDirection(planarToPlayer, transform.forward);
        Vector3 right = SafeDirection(
            Vector3.Cross(Vector3.up, planarToPlayer),
            transform.right);
        Vector3[] candidates =
        {
            Vector3.up,
            (Vector3.up * 1.35f + planarToPlayer * 0.45f).normalized,
            (Vector3.up * 1.35f - planarToPlayer * 0.45f).normalized,
            (Vector3.up * 1.25f + right * 0.55f).normalized,
            (Vector3.up * 1.25f - right * 0.55f).normalized
        };
        float bestClearance = float.NegativeInfinity;
        foreach (Vector3 candidate in candidates)
        {
            float clearance = ProbeClearance(bounds, candidate);
            if (clearance <= bestClearance)
                continue;
            bestClearance = clearance;
            direction = candidate;
        }
        return bestClearance >=
               ModularBossCombatPolicy.MinimumVerticalEscapeClearance;
    }

    void ResetAvoidanceProgressObservation()
    {
        avoidanceProgressPosition = Vector3.zero;
        avoidanceProgressStartedAt = -1f;
    }

    bool HasLiveBlockingCoverTarget()
    {
        return blockingCoverBuilding != null &&
               !blockingCoverBuilding.IsUrbanDestroyed;
    }

    bool HasLiveCoverBreachTarget()
    {
        return coverBreachBuilding != null &&
               !coverBreachBuilding.IsUrbanDestroyed;
    }

    void BeginRecovery(float now)
    {
        BeginRecovery(now, ModularBossObstaclePolicy.RecoverySeconds);
    }

    void BeginRecovery(float now, float duration)
    {
        resumeBacktrackAfterCoverBreach = false;
        ClearBacktrackTarget();
        SetObstacleState(ModularBossObstacleState.Recovery);
        stateEndsAt = now + Mathf.Max(0.1f, duration);
    }

    void BeginBuildingEscapeRecovery(float now, Vector3 impactDirection)
    {
        resumeBacktrackAfterCoverBreach = false;
        Vector3 away = -Vector3.ProjectOnPlane(
            impactDirection,
            Vector3.up);
        recoveryEscapeDirection = SafeDirection(
            Vector3.up * 1.25f + away * 0.7f,
            Vector3.up);
        recoveryEscapeEndsAt = now + 1.35f;
        if (motion != null && body != null)
        {
            Vector3 velocityChange = Vector3.up * 7f +
                                     SafeDirection(away, -transform.forward) *
                                     3f;
            motion.QueueExternalImpulse(
                velocityChange * body.mass,
                body.worldCenterOfMass);
        }
        if (!TryBeginBacktrackEscape(now))
            BeginRecovery(now, 1.45f);
    }

    void TryRecordSafeNavigationPoint(bool force)
    {
        if (body == null || structureGraph == null ||
            (!force &&
             obstacleState != ModularBossObstacleState.Pursuit &&
             obstacleState != ModularBossObstacleState.LocalAvoidance) ||
            (!force && Time.time - lastWorldCollisionAt <= 0.55f))
        {
            return;
        }

        Bounds bounds = structureGraph.ResolveVisualBounds();
        Vector3 extents = bounds.extents + Vector3.one * 1.5f;
        if (!IsNavigationVolumeClear(bounds.center, extents))
            return;
        if (safeNavigationTrail.Count > 0 &&
            Vector3.Distance(
                safeNavigationTrail[safeNavigationTrail.Count - 1],
                bounds.center) <
            ModularBossCombatPolicy.SafeNavigationPointSpacing)
        {
            return;
        }

        safeNavigationTrail.Add(bounds.center);
        if (safeNavigationTrail.Count >
            ModularBossCombatPolicy.SafeNavigationTrailCapacity)
        {
            safeNavigationTrail.RemoveAt(0);
        }
    }

    bool IsNavigationVolumeClear(Vector3 center, Vector3 extents)
    {
        int hitCount = Physics.OverlapBoxNonAlloc(
            center,
            extents,
            spawnOverlapBuffer,
            Quaternion.identity,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        if (hitCount >= spawnOverlapBuffer.Length)
            return false;
        for (int index = 0; index < hitCount; index++)
        {
            Collider collider = spawnOverlapBuffer[index];
            if (collider == null || !collider.enabled ||
                collider.transform.IsChildOf(transform) ||
                (playerBody != null &&
                 collider.transform.IsChildOf(playerBody.transform)))
            {
                continue;
            }
            if (IsUrbanCover(collider))
                return false;
        }
        return true;
    }

    bool TryBeginBacktrackEscape(float now)
    {
        if (body == null || structureGraph == null ||
            safeNavigationTrail.Count == 0)
        {
            return false;
        }

        int startIndex = backtrackTargetIndex >= 0
            ? Mathf.Min(backtrackTargetIndex,
                safeNavigationTrail.Count - 1)
            : safeNavigationTrail.Count - 1;
        return TrySelectBacktrackTarget(now, startIndex);
    }

    bool TrySelectBacktrackTarget(float now, int startIndex)
    {
        Bounds bounds = structureGraph.ResolveVisualBounds();
        Vector3 extents = bounds.extents + Vector3.one * 1.5f;
        for (int index = Mathf.Min(startIndex,
                 safeNavigationTrail.Count - 1);
             index >= 0;
             index--)
        {
            Vector3 candidate = safeNavigationTrail[index];
            if (Vector3.Distance(bounds.center, candidate) <
                    ModularBossCombatPolicy.BacktrackMinimumTargetDistance ||
                !IsNavigationVolumeClear(candidate, extents))
            {
                continue;
            }

            backtrackTargetIndex = index;
            backtrackTarget = candidate;
            backtrackProgressPosition = bounds.center;
            backtrackProgressStartedAt = now;
            SetObstacleState(ModularBossObstacleState.BacktrackEscape);
            stateEndsAt = now +
                ModularBossCombatPolicy.BacktrackEscapeSeconds;
            return true;
        }
        return false;
    }

    void UpdateBacktrackEscape(float now)
    {
        if (body == null || structureGraph == null ||
            backtrackTargetIndex < 0)
        {
            BeginRecovery(now, 0.65f);
            return;
        }

        Bounds bounds = structureGraph.ResolveVisualBounds();
        Vector3 current = bounds.center;
        if (Vector3.Distance(current, backtrackTarget) <=
            ModularBossCombatPolicy.BacktrackArrivalDistance)
        {
            BeginRecovery(now, 0.65f);
            return;
        }

        if (Vector3.Distance(current, backtrackProgressPosition) >=
            ModularBossCombatPolicy.BuildingStuckProgressDistance)
        {
            backtrackProgressPosition = current;
            backtrackProgressStartedAt = now;
        }

        bool progressTimedOut = backtrackProgressStartedAt >= 0f &&
            now - backtrackProgressStartedAt >=
            ModularBossCombatPolicy.BuildingStuckBreachSeconds;
        if (progressTimedOut)
        {
            bool liveBuildingContact =
                now - lastWorldCollisionAt <= 0.55f &&
                contactedBuilding != null &&
                !contactedBuilding.IsUrbanDestroyed &&
                contactedBuildingCollider != null;
            if (liveBuildingContact)
            {
                BeginCoverBreach(
                    now,
                    contactedBuilding,
                    contactedBuildingCollider,
                    true);
                return;
            }
            if (TrySelectBacktrackTarget(
                    now,
                    backtrackTargetIndex - 1))
            {
                return;
            }
            BeginRecovery(now, 0.65f);
            return;
        }

        if (now >= stateEndsAt)
        {
            if (!TrySelectBacktrackTarget(
                    now,
                    backtrackTargetIndex - 1))
            {
                BeginRecovery(now, 0.65f);
            }
        }
    }

    void ClearBacktrackTarget()
    {
        backtrackTargetIndex = -1;
        backtrackTarget = Vector3.zero;
        backtrackProgressPosition = Vector3.zero;
        backtrackProgressStartedAt = -1f;
    }

    void CompleteCoverBreachAttempt(float now)
    {
        bool shouldResumeBacktrack = resumeBacktrackAfterCoverBreach;
        resumeBacktrackAfterCoverBreach = false;
        coverBreachBuilding = null;
        coverBreachCollider = null;
        if (shouldResumeBacktrack && TryBeginBacktrackEscape(now))
            return;
        BeginRecovery(now);
    }

    void ResetSafeNavigationTrail()
    {
        safeNavigationTrail.Clear();
        ClearBacktrackTarget();
    }

    void SetObstacleState(ModularBossObstacleState value)
    {
        obstacleState = value;
        obstacleStateStartedAt = Time.time;
        bool ramWarning = value ==
                          ModularBossObstacleState.PlayerRamTelegraph ||
                          value == ModularBossObstacleState.PlayerRamCharge ||
                          value ==
                          ModularBossObstacleState.CoverBreachTelegraph ||
                          value ==
                          ModularBossObstacleState.CoverBreachCharge;
        readability?.SetRamWarning(ramWarning);
    }

    void ReleaseBridgeObstacleTracking()
    {
        contactedBridge = null;
        lockedBridge = null;
        probedBlockingBridge = null;
        lastBridgeContactPoint = Vector3.zero;
        probedBridgePoint = Vector3.zero;
        avoidanceDirection = Vector3.zero;
        bridgeContactStartedAt = -1f;
        lastBridgeContactAt = -1f;
        contactedBridgeScore = float.NegativeInfinity;
        bridgeContactConfirmedAt = -1f;
        forceBridgeApproach = false;
    }

    void ResetObstacleRuntime()
    {
        ReleaseBridgeObstacleTracking();
        ClearFocusedPlayerTarget();
        ResetSafeNavigationTrail();
        stateEndsAt = 0f;
        immunityEndsAt = 0f;
        ceaseFireWindowCount = 0;
        directLineBlocked = false;
        nearbyUrbanCover = false;
        playerEntrenchedInCover = false;
        blockingCoverBuilding = null;
        blockingCoverRuin = null;
        blockingCoverCollider = null;
        contactedBuilding = null;
        contactedBuildingCollider = null;
        ResetStuckBuildingObservation();
        nextAwarenessProbeAt = 0f;
        coverObservedAt = -1f;
        coveredSince = -1f;
        coverBreachBuilding = null;
        coverBreachCollider = null;
        resumeBacktrackAfterCoverBreach = false;
        committedAvoidanceDirection = Vector3.zero;
        avoidanceCommitEndsAt = -1f;
        ResetAvoidanceProgressObservation();
        collisionEscapeDirection = Vector3.zero;
        lastWorldCollisionAt = -1f;
        recoveryEscapeDirection = Vector3.zero;
        recoveryEscapeEndsAt = -1f;
        committedRamDirection = Vector3.zero;
        committedRamTarget = Vector3.zero;
        SetObstacleState(ModularBossObstacleState.Pursuit);
    }

    bool TryResolveLocalAvoidance(
        out Vector3 direction,
        out UrbanDestructibleBridge blockingBridge,
        out Vector3 blockingPoint)
    {
        direction = Vector3.zero;
        blockingBridge = null;
        blockingPoint = Vector3.zero;
        if (structureGraph == null || playerBody == null)
            return false;
        Bounds bounds = structureGraph.ResolveVisualBounds();
        Vector3 toPlayer = playerBody.worldCenterOfMass - bounds.center;
        if (toPlayer.sqrMagnitude < 0.001f)
            return false;
        Vector3 desired = toPlayer.normalized;
        float directClearance = ProbeClearance(
            bounds,
            desired,
            out blockingBridge,
            out blockingPoint);
        if (directClearance >= 0.98f)
            return false;

        Vector3 best = desired;
        float bestScore = directClearance + 0.35f;
        Vector3 planarDesired = Vector3.ProjectOnPlane(
            desired,
            Vector3.up);
        if (planarDesired.sqrMagnitude < 0.001f)
            planarDesired = Vector3.forward;
        else
            planarDesired.Normalize();
        Vector3 stableRight = Vector3.Cross(
            Vector3.up,
            planarDesired).normalized;
        EvaluateAvoidanceDirection(
            bounds,
            Quaternion.AngleAxis(28f, Vector3.up) * desired,
            desired,
            ref best,
            ref bestScore);
        EvaluateAvoidanceDirection(
            bounds,
            Quaternion.AngleAxis(-28f, Vector3.up) * desired,
            desired,
            ref best,
            ref bestScore);
        EvaluateAvoidanceDirection(
            bounds,
            Quaternion.AngleAxis(55f, Vector3.up) * desired,
            desired,
            ref best,
            ref bestScore);
        EvaluateAvoidanceDirection(
            bounds,
            Quaternion.AngleAxis(-55f, Vector3.up) * desired,
            desired,
            ref best,
            ref bestScore);
        EvaluateAvoidanceDirection(
            bounds,
            Quaternion.AngleAxis(82f, Vector3.up) * desired,
            desired,
            ref best,
            ref bestScore);
        EvaluateAvoidanceDirection(
            bounds,
            Quaternion.AngleAxis(-82f, Vector3.up) * desired,
            desired,
            ref best,
            ref bestScore);
        EvaluateAvoidanceDirection(
            bounds,
            (desired + stableRight * 0.9f).normalized,
            desired,
            ref best,
            ref bestScore);
        EvaluateAvoidanceDirection(
            bounds,
            (desired - stableRight * 0.9f).normalized,
            desired,
            ref best,
            ref bestScore);
        EvaluateAvoidanceDirection(
            bounds,
            (desired + Vector3.up * 0.82f).normalized,
            desired,
            ref best,
            ref bestScore);
        EvaluateAvoidanceDirection(
            bounds,
            (desired - Vector3.up * 0.68f).normalized,
            desired,
            ref best,
            ref bestScore);
        EvaluateAvoidanceDirection(
            bounds,
            stableRight,
            desired,
            ref best,
            ref bestScore);
        EvaluateAvoidanceDirection(
            bounds,
            -stableRight,
            desired,
            ref best,
            ref bestScore);
        EvaluateAvoidanceDirection(
            bounds,
            Vector3.up,
            desired,
            ref best,
            ref bestScore);
        direction = best;
        return true;
    }

    bool TryResolveSightlineDetour(out Vector3 direction)
    {
        direction = Vector3.zero;
        if (structureGraph == null || playerBody == null ||
            blockingCoverCollider == null)
        {
            return false;
        }

        Bounds bossBounds = structureGraph.ResolveVisualBounds();
        Bounds obstacleBounds = blockingCoverBuilding != null &&
                                !blockingCoverBuilding.IsUrbanDestroyed
            ? blockingCoverBuilding.DestructionBounds
            : blockingCoverRuin != null &&
              !blockingCoverRuin.IsUrbanDestroyed
                ? blockingCoverRuin.DestructionBounds
                : blockingCoverCollider.bounds;
        Vector3 planarToPlayer = Vector3.ProjectOnPlane(
            playerBody.worldCenterOfMass - bossBounds.center,
            Vector3.up);
        if (planarToPlayer.sqrMagnitude < 0.001f)
            return false;
        planarToPlayer.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, planarToPlayer).normalized;
        float obstacleHalfWidth =
            Mathf.Abs(right.x) * obstacleBounds.extents.x +
            Mathf.Abs(right.z) * obstacleBounds.extents.z;
        float bossRadius = Mathf.Max(
            bossBounds.extents.x,
            bossBounds.extents.z);
        float sideOffset = obstacleHalfWidth + bossRadius + 18f;
        Vector3 leftPoint = obstacleBounds.center - right * sideOffset;
        Vector3 rightPoint = obstacleBounds.center + right * sideOffset;
        leftPoint.y = bossBounds.center.y;
        rightPoint.y = bossBounds.center.y;
        Vector3 abovePoint = obstacleBounds.center + Vector3.up *
            (obstacleBounds.extents.y + bossBounds.extents.y + 12f);

        Vector3 desired = SafeDirection(
            playerBody.worldCenterOfMass - bossBounds.center,
            transform.forward);
        Vector3 best = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        EvaluateDetourWaypoint(
            bossBounds,
            leftPoint,
            desired,
            ref best,
            ref bestScore);
        EvaluateDetourWaypoint(
            bossBounds,
            rightPoint,
            desired,
            ref best,
            ref bestScore);
        EvaluateDetourWaypoint(
            bossBounds,
            abovePoint,
            desired,
            ref best,
            ref bestScore);
        if (best.sqrMagnitude < 0.001f)
            return false;
        direction = best;
        return true;
    }

    void EvaluateDetourWaypoint(
        Bounds bossBounds,
        Vector3 waypoint,
        Vector3 desired,
        ref Vector3 best,
        ref float bestScore)
    {
        Vector3 candidate = waypoint - bossBounds.center;
        if (candidate.sqrMagnitude < 0.001f)
            return;
        candidate.Normalize();
        float clearance = ProbeClearance(bossBounds, candidate);
        float progress = Vector3.Dot(candidate, desired);
        float verticalPenalty = Mathf.Abs(candidate.y) * 0.08f;
        float score = clearance * 2f + progress * 0.45f -
                      verticalPenalty;
        if (score <= bestScore)
            return;
        bestScore = score;
        best = candidate;
    }

    Vector3 CommitAvoidanceDirection(Vector3 candidate, float now)
    {
        Bounds bossBounds = structureGraph.ResolveVisualBounds();
        if (committedAvoidanceDirection.sqrMagnitude > 0.001f &&
            now < avoidanceCommitEndsAt &&
            ProbeClearance(bossBounds, committedAvoidanceDirection) > 0.16f)
        {
            return committedAvoidanceDirection;
        }
        committedAvoidanceDirection = SafeDirection(
            candidate,
            transform.forward);
        avoidanceCommitEndsAt = now + 1.1f;
        return committedAvoidanceDirection;
    }

    bool IsBridgeBlockingPursuit(UrbanDestructibleBridge bridge)
    {
        if (bridge == null || bridge.IsUrbanDestroyed ||
            structureGraph == null || playerBody == null)
        {
            return false;
        }
        Bounds bossBounds = structureGraph.ResolveVisualBounds();
        float tolerance = Mathf.Max(
            4f,
            Mathf.Min(bossBounds.extents.x, bossBounds.extents.z) * 0.2f);
        return ModularBossObstaclePolicy.IsBridgeBetweenBossAndPlayer(
            bossBounds.center,
            playerBody.worldCenterOfMass,
            bridge.DestructionBounds,
            tolerance);
    }

    void EvaluateAvoidanceDirection(
        Bounds bounds,
        Vector3 candidate,
        Vector3 desired,
        ref Vector3 best,
        ref float bestScore)
    {
        float score = ProbeClearance(bounds, candidate) +
                      Mathf.Max(-0.2f, Vector3.Dot(candidate, desired)) *
                      0.35f;
        if (score <= bestScore)
            return;
        bestScore = score;
        best = candidate;
    }

    float ProbeClearance(Bounds bounds, Vector3 direction)
    {
        return ProbeClearance(
            bounds,
            direction,
            out _,
            out _);
    }

    float ProbeClearance(
        Bounds bounds,
        Vector3 direction,
        out UrbanDestructibleBridge blockingBridge,
        out Vector3 blockingPoint)
    {
        blockingBridge = null;
        blockingPoint = Vector3.zero;
        Vector3 halfExtents = bounds.extents * 0.78f;
        halfExtents.x = Mathf.Max(0.75f, halfExtents.x);
        halfExtents.y = Mathf.Max(0.75f, halfExtents.y);
        halfExtents.z = Mathf.Max(0.75f, halfExtents.z);
        int hitCount = Physics.BoxCastNonAlloc(
            bounds.center,
            halfExtents,
            direction,
            ObstacleProbeHits,
            Quaternion.identity,
            ObstacleProbeDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        float nearest = ObstacleProbeDistance;
        for (int index = 0; index < hitCount; index++)
        {
            Collider collider = ObstacleProbeHits[index].collider;
            if (collider == null ||
                collider.transform.IsChildOf(transform) ||
                (playerBody != null &&
                 collider.transform.IsChildOf(playerBody.transform)))
            {
                continue;
            }
            UrbanDestructibleBridge bridge =
                collider.GetComponentInParent<UrbanDestructibleBridge>();
            if (bridge != null && bridge.IsUrbanDestroyed)
                continue;
            if (ObstacleProbeHits[index].distance >= nearest)
                continue;
            nearest = ObstacleProbeHits[index].distance;
            blockingBridge = bridge;
            blockingPoint = ObstacleProbeHits[index].point;
            if (blockingBridge != null &&
                blockingPoint.sqrMagnitude < 0.001f)
            {
                blockingPoint = blockingBridge.DestructionBounds.
                    ClosestPoint(bounds.center);
            }
        }
        return Mathf.Clamp01(nearest / ObstacleProbeDistance);
    }

    static Vector3 SafeDirection(Vector3 value, Vector3 fallback)
    {
        if (value.sqrMagnitude > 0.0001f)
            return value.normalized;
        if (fallback.sqrMagnitude > 0.0001f)
            return fallback.normalized;
        return Vector3.forward;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!combatActive || collision == null || collision.contactCount == 0)
            return;
        ContactPoint contact = collision.GetContact(0);
        float speed = collision.relativeVelocity.magnitude;
        if (TryApplyPlayerRamCollision(collision, contact, speed))
            return;
        if (!TryApplyIntentionalBuildingRam(collision, contact, speed))
            RememberWorldCollision(collision, contact);
    }

    bool TryApplyPlayerRamCollision(
        Collision collision,
        ContactPoint contact,
        float speed)
    {
        if (obstacleState != ModularBossObstacleState.PlayerRamCharge ||
            Time.time < nextPlayerImpactAt || playerBody == null ||
            speed < 10f)
        {
            return false;
        }
        GridModuleView target = ResolveVehicleModule(
            collision.collider,
            playerBody.transform) ?? ResolveVehicleModule(
            contact.thisCollider,
            playerBody.transform) ?? ResolveVehicleModule(
            contact.otherCollider,
            playerBody.transform);
        if (target?.Record == null)
            return false;
        float damage = ModularBossCombatPolicy.ResolvePlayerRamDamage(
            playerGraph.MaximumIntegrity(target.Record.RuntimeId),
            speed,
            build.Profile.Tier);
        Vector3 impulseDirection = SafeDirection(
            body.velocity,
            committedRamDirection);
        playerGraph.ApplyDamage(
            target.Record.RuntimeId,
            new SpaceDamageInfo(
                damage,
                contact.point,
                impulseDirection * Mathf.Max(damage, collision.impulse.magnitude),
                SpaceDamageType.Collision,
                gameObject));
        nextPlayerImpactAt = Time.time + 1f;
        BeginRecovery(
            Time.time,
            ModularBossCombatPolicy.PlayerRamRecoverySeconds);
        return true;
    }

    bool TryApplyIntentionalBuildingRam(
        Collision collision,
        ContactPoint contact,
        float speed)
    {
        bool playerRamTail = Time.time - lastCommittedPlayerRamAt <=
            ModularBossCombatPolicy.PlayerRamChargeSeconds + 0.85f;
        bool intentionalRam =
            obstacleState == ModularBossObstacleState.PlayerRamCharge ||
            obstacleState == ModularBossObstacleState.CoverBreachCharge ||
            (obstacleState == ModularBossObstacleState.Recovery &&
             playerRamTail);
        float minimumImpactSpeed =
            obstacleState == ModularBossObstacleState.CoverBreachCharge &&
            Time.time - obstacleStateStartedAt >= 0.45f
                ? 0f
                : 14f;
        if (!intentionalRam || speed < minimumImpactSpeed)
            return false;

        Collider urbanCollider = ResolveBuildingCollisionCollider(collision);
        UrbanDestructibleBuilding building =
            ResolveUrbanBuilding(urbanCollider);
        // A fallen half is debris/cover, not another breach target. Repeatedly
        // cutting ruin sections made a low-flying Boss chew the lower half
        // instead of escaping upward and resuming the hunt.
        if (building == null || building.IsUrbanDestroyed)
            return false;

        Vector3 impactDirection = SafeDirection(
            body.velocity,
            committedRamDirection);
        bool shieldProtected = shield != null &&
            shield.ApplyEnvironmentalImpact(
                ModularBossCombatPolicy.ResolveBuildingShieldDamageFraction(
                    speed),
                contact.point,
                building.gameObject);
        if (!shieldProtected)
        {
            ApplyBossBuildingCollisionLoss(
                contact.point,
                impactDirection,
                collision.impulse,
                speed,
                building.gameObject);
        }
        bool collapsed = UrbanDestructionWorld.TryApplyEnergyBlade(
            urbanCollider,
            contact.point,
            contact.normal,
            impactDirection,
            1000f,
            Mathf.Max(
                30f,
                structureGraph.ResolveVisualBounds().size.x * 0.72f),
            gameObject);
        if (collapsed)
        {
            coverBreachBuilding = null;
            coverBreachCollider = null;
            contactedBuilding = null;
            contactedBuildingCollider = null;
            ResetStuckBuildingObservation();
            coveredSince = -1f;
            BeginBuildingEscapeRecovery(
                Time.time,
                impactDirection);
        }
        return collapsed;
    }

    Collider ResolveBuildingCollisionCollider(Collision collision)
    {
        Collider collider = collision.collider;
        if (ResolveUrbanBuilding(collider) != null)
            return collider;
        if (collision.contactCount <= 0)
            return null;
        ContactPoint contact = collision.GetContact(0);
        collider = contact.thisCollider;
        if (ResolveUrbanBuilding(collider) != null)
            return collider;
        collider = contact.otherCollider;
        if (ResolveUrbanBuilding(collider) != null)
            return collider;
        return null;
    }

    static GridModuleView ResolveVehicleModule(
        Collider collider,
        Transform vehicleRoot)
    {
        if (collider == null || vehicleRoot == null ||
            !collider.transform.IsChildOf(vehicleRoot))
        {
            return null;
        }
        return collider.GetComponentInParent<GridModuleView>();
    }

    void ApplyBossBuildingCollisionLoss(
        Vector3 point,
        Vector3 direction,
        Vector3 collisionImpulse,
        float speed,
        GameObject source)
    {
        if (Time.time < nextBossBuildingLossAt || presenter == null ||
            structureGraph == null)
        {
            return;
        }
        int requested = ModularBossCombatPolicy.
            ResolveBossCollisionModuleLoss(speed);
        if (requested <= 0)
            return;
        nextBossBuildingLossAt = Time.time + 0.85f;
        for (int index = 0; index < collisionModuleCandidates.Length; index++)
        {
            collisionModuleCandidates[index] = null;
            collisionModuleCandidateDistances[index] = float.PositiveInfinity;
        }
        foreach (GridModuleView view in presenter.Views.Values)
        {
            if (view?.Record == null ||
                view.Record.RuntimeId == GridAssemblyModel.CoreRuntimeId)
            {
                continue;
            }
            Collider moduleCollider =
                view.GetComponentInChildren<Collider>(true);
            Vector3 nearestPoint = moduleCollider != null
                ? moduleCollider.ClosestPoint(point)
                : view.transform.position;
            float distance = (nearestPoint - point).sqrMagnitude;
            for (int slot = 0; slot < requested; slot++)
            {
                if (distance >= collisionModuleCandidateDistances[slot])
                    continue;
                for (int shift = requested - 1; shift > slot; shift--)
                {
                    collisionModuleCandidates[shift] =
                        collisionModuleCandidates[shift - 1];
                    collisionModuleCandidateDistances[shift] =
                        collisionModuleCandidateDistances[shift - 1];
                }
                collisionModuleCandidates[slot] = view;
                collisionModuleCandidateDistances[slot] = distance;
                break;
            }
        }

        structureGraph.BeginDamageBatch();
        try
        {
            for (int index = 0; index < requested; index++)
            {
                GridModuleView view = collisionModuleCandidates[index];
                if (view?.Record == null)
                    continue;
                string runtimeId = view.Record.RuntimeId;
                float lethalDamage =
                    structureGraph.MaximumIntegrity(runtimeId) + 1f;
                structureGraph.ApplyDamage(
                    runtimeId,
                    new SpaceDamageInfo(
                        lethalDamage,
                        point,
                        SafeDirection(direction, transform.forward) *
                        Mathf.Max(lethalDamage, collisionImpulse.magnitude),
                        SpaceDamageType.Collision,
                        source));
            }
        }
        finally
        {
            structureGraph.EndDamageBatch();
        }
    }

    void OnCollisionStay(Collision collision)
    {
        if (!combatActive || collision == null || collision.collider == null)
            return;
        if (collision.contactCount > 0 &&
            TryApplyIntentionalBuildingRam(
                collision,
                collision.GetContact(0),
                collision.relativeVelocity.magnitude))
        {
            return;
        }
        if (collision.contactCount > 0 &&
            RememberWorldCollision(collision, collision.GetContact(0)))
        {
            return;
        }
        UrbanDestructibleBridge bridge =
            collision.collider.GetComponentInParent<UrbanDestructibleBridge>();
        if (bridge == null || bridge.IsUrbanDestroyed ||
            !IsBridgeBlockingPursuit(bridge))
            return;
        Vector3 contactPoint = collision.contactCount > 0
            ? collision.GetContact(0).point
            : bridge.DestructionBounds.ClosestPoint(body.worldCenterOfMass);
        shield?.ApplyEnvironmentalImpact(
            ModularBossCombatPolicy.ResolveBridgeShieldDamageFraction(
                collision.relativeVelocity.magnitude),
            contactPoint,
            bridge.gameObject);
        float now = Time.time;
        if (lockedBridge != null && lockedBridge != bridge &&
            !lockedBridge.IsUrbanDestroyed &&
            now - lastBridgeContactAt <= BridgeContactGraceSeconds)
        {
            return;
        }
        float forwardDepth = Vector3.Dot(
            contactPoint - body.worldCenterOfMass,
            transform.forward);
        float contactScore = forwardDepth * 0.65f -
                             Vector3.Distance(
                                 contactPoint,
                                 body.worldCenterOfMass) * 0.35f;
        bool stale = now - lastBridgeContactAt > BridgeContactGraceSeconds;
        if (contactedBridge != bridge && !stale &&
            contactScore <= contactedBridgeScore)
        {
            return;
        }
        if (contactedBridge != bridge || stale)
        {
            contactedBridge = bridge;
            bridgeContactStartedAt = now;
        }
        contactedBridgeScore = contactScore;
        lastBridgeContactAt = now;
        lastBridgeContactPoint = contactPoint;
    }

    bool RememberWorldCollision(
        Collision collision,
        ContactPoint contact)
    {
        Collider urbanCollider = ResolveBuildingCollisionCollider(collision);
        if (urbanCollider == null)
        {
            contactedBuilding = null;
            contactedBuildingCollider = null;
            Collider candidate = collision.collider;
            UrbanDestructibleRuinSection ruin = ResolveUrbanRuin(candidate);
            if (ruin == null || ruin.IsUrbanDestroyed)
                return false;
        }
        else
        {
            UrbanDestructibleBuilding building =
                ResolveUrbanBuilding(urbanCollider);
            if (building != null && !building.IsUrbanDestroyed)
            {
                contactedBuilding = building;
                contactedBuildingCollider = urbanCollider;
            }
            else
            {
                contactedBuilding = null;
                contactedBuildingCollider = null;
            }
        }
        Vector3 away = contact.normal;
        if (away.sqrMagnitude < 0.001f)
            away = -body.velocity;
        collisionEscapeDirection = SafeDirection(
            away + Vector3.up * 0.55f,
            Vector3.up);
        lastWorldCollisionAt = Time.time;
        return true;
    }

    void UpdatePilotControl()
    {
        Vector3 up = Vector3.up;
        Vector3 toPlayer = playerBody.worldCenterOfMass -
                           body.worldCenterOfMass;
        Vector3 planar = Vector3.ProjectOnPlane(toPlayer, up);
        float distance = planar.magnitude;
        Vector3 desiredAim = toPlayer.sqrMagnitude > 0.001f
            ? toPlayer.normalized
            : transform.forward;
        if (smoothedAimForward.sqrMagnitude < 0.001f)
            smoothedAimForward = transform.forward;
        float thrusterRatio = Mathf.Clamp01(
            LiveThrusterCount /
            (float)Mathf.Max(1, InitialThrusterCount));
        float turnRate = Mathf.Lerp(0.32f, 0.72f, thrusterRatio);
        if (emergencyAssist)
            turnRate = Mathf.Min(turnRate, 0.46f);
        smoothedAimForward = Vector3.RotateTowards(
            smoothedAimForward,
            desiredAim,
            turnRate * Time.fixedDeltaTime,
            0f).normalized;
        Vector3 aim = smoothedAimForward;
        float rangeError = distance - build.Profile.PreferredCombatRadius;
        float forward = Mathf.Clamp(rangeError / 45f, -0.75f, 1f);
        float orbit = Mathf.Sin(Time.time * 0.34f + build.Profile.Tier) *
                      0.62f;
        Bounds bossBounds = structureGraph.ResolveVisualBounds();
        float desiredHeight = Mathf.Max(
            22f + build.Profile.Tier * 2f,
            bossBounds.extents.y + 14f);
        // Keep the encounter genuinely three-dimensional. The offset is an
        // altitude lane, not a visual bob: cover raises the lane and the slow
        // orbit makes both upward and downward thrusters participate in pursuit.
        float altitudeOrbit = Mathf.Sin(
            Time.time * 0.22f + build.Profile.Tier * 1.37f) *
            Mathf.Lerp(
                7f,
                13f,
                Mathf.Clamp01(build.Profile.Tier / 5f));
        if (nearbyUrbanCover || directLineBlocked)
            altitudeOrbit += 7f;
        desiredHeight += altitudeOrbit;
        float heightError =
            (playerBody.worldCenterOfMass.y + desiredHeight) -
            body.worldCenterOfMass.y;
        float vertical = Mathf.Clamp(heightError / 18f, -1f, 1f);
        float mobilityScale =
            ModularBossCombatPolicy.ResolveDamagedMobility(
                LiveThrusterCount,
                InitialThrusterCount);
        if (emergencyAssist)
            mobilityScale = Mathf.Min(mobilityScale, 0.46f);

        if (obstacleState == ModularBossObstacleState.PlayerRamTelegraph ||
            obstacleState == ModularBossObstacleState.CoverBreachTelegraph)
        {
            Vector3 telegraphAim = obstacleState ==
                ModularBossObstacleState.PlayerRamTelegraph
                ? SafeDirection(committedRamDirection, aim)
                : SafeDirection(
                    coverBreachPoint - body.worldCenterOfMass,
                    aim);
            motion.SetInjectedControl(new RobocraftControlFrame
            {
                move = new Vector2(0f, -0.18f * mobilityScale),
                vertical = Mathf.Clamp(telegraphAim.y, -0.45f, 0.45f) *
                           mobilityScale,
                roll = 0f,
                braking = true,
                boost = false,
                freeLook = false,
                aimForwardWorld = telegraphAim,
                hasAimOverride = true
            });
            return;
        }

        if (obstacleState == ModularBossObstacleState.PlayerRamCharge ||
            obstacleState == ModularBossObstacleState.CoverBreachCharge)
        {
            Vector3 chargeAim = obstacleState ==
                ModularBossObstacleState.PlayerRamCharge
                ? SafeDirection(committedRamDirection, aim)
                : SafeDirection(
                    coverBreachPoint - body.worldCenterOfMass,
                    aim);
            motion.SetInjectedControl(new RobocraftControlFrame
            {
                move = new Vector2(0f, mobilityScale),
                vertical = Mathf.Clamp(chargeAim.y * 1.35f, -1f, 1f) *
                           mobilityScale,
                roll = 0f,
                braking = false,
                boost = !emergencyAssist,
                freeLook = false,
                aimForwardWorld = chargeAim,
                hasAimOverride = true
            });
            return;
        }

        if (obstacleState == ModularBossObstacleState.BlockedCeaseFire)
        {
            Vector3 brakeAxes =
                ModularBossSteeringPolicy.ToAimRelativeAxes(
                    -body.velocity,
                    aim,
                    up);
            motion.SetInjectedControl(new RobocraftControlFrame
            {
                move = new Vector2(
                    Mathf.Clamp(brakeAxes.x / 12f, -1f, 1f),
                    Mathf.Clamp(brakeAxes.z / 12f, -1f, 1f)),
                vertical = Mathf.Clamp(
                    brakeAxes.y / 10f,
                    -1f,
                    1f),
                braking = true,
                boost = false,
                freeLook = false,
                aimForwardWorld = aim,
                hasAimOverride = true
            });
            return;
        }

        if (obstacleState == ModularBossObstacleState.RamCharge)
        {
            Vector3 ramAim = lockedBridge != null &&
                             !lockedBridge.IsUrbanDestroyed
                ? (lockedBridge.DestructionBounds.center -
                   body.worldCenterOfMass).normalized
                : aim;
            motion.SetInjectedControl(new RobocraftControlFrame
            {
                move = new Vector2(0f, mobilityScale),
                vertical = Mathf.Clamp(ramAim.y * 1.4f, -1f, 1f) *
                           mobilityScale,
                roll = 0f,
                braking = false,
                boost = !emergencyAssist,
                freeLook = false,
                aimForwardWorld = ramAim,
                hasAimOverride = true
            });
            return;
        }

        if (obstacleState == ModularBossObstacleState.BacktrackEscape)
        {
            Vector3 toSafePoint = backtrackTarget - bossBounds.center;
            Vector3 escapeDirection = SafeDirection(
                toSafePoint,
                recoveryEscapeDirection);
            Vector3 escapeAxes =
                ModularBossSteeringPolicy.ToAimRelativeAxes(
                    escapeDirection,
                    aim,
                    up);
            motion.SetInjectedControl(new RobocraftControlFrame
            {
                move = new Vector2(
                    Mathf.Clamp(escapeAxes.x * 1.25f, -1f, 1f),
                    Mathf.Clamp(escapeAxes.z * 1.15f, -1f, 1f)) *
                    mobilityScale,
                vertical = Mathf.Clamp(
                    escapeAxes.y * 1.35f,
                    -1f,
                    1f) * mobilityScale,
                roll = emergencyAssist
                    ? Mathf.Sin(Time.time * 1.7f) * 0.08f
                    : 0f,
                braking = false,
                boost = !emergencyAssist &&
                        toSafePoint.magnitude > 30f,
                freeLook = false,
                aimForwardWorld = aim,
                hasAimOverride = true
            });
            return;
        }

        if (obstacleState == ModularBossObstacleState.Recovery)
        {
            if (Time.time < recoveryEscapeEndsAt &&
                recoveryEscapeDirection.sqrMagnitude > 0.001f)
            {
                Vector3 escapeAxes =
                    ModularBossSteeringPolicy.ToAimRelativeAxes(
                        recoveryEscapeDirection,
                        aim,
                        up);
                motion.SetInjectedControl(new RobocraftControlFrame
                {
                    move = new Vector2(
                        Mathf.Clamp(escapeAxes.x * 1.25f, -1f, 1f),
                        Mathf.Clamp(escapeAxes.z, -1f, 0.4f)) *
                        mobilityScale,
                    vertical = Mathf.Clamp(
                        escapeAxes.y * 1.4f,
                        0.35f,
                        1f) * mobilityScale,
                    roll = 0f,
                    braking = false,
                    boost = false,
                    freeLook = false,
                    aimForwardWorld = aim,
                    hasAimOverride = true
                });
                return;
            }
            motion.SetInjectedControl(new RobocraftControlFrame
            {
                move = new Vector2(0f, -0.32f * mobilityScale),
                vertical = 0.18f * mobilityScale,
                roll = emergencyAssist
                    ? Mathf.Sin(Time.time * 1.7f) * 0.08f
                    : 0f,
                braking = true,
                boost = false,
                freeLook = false,
                aimForwardWorld = aim,
                hasAimOverride = true
            });
            return;
        }

        if (obstacleState == ModularBossObstacleState.LocalAvoidance &&
            avoidanceDirection.sqrMagnitude > 0.001f)
        {
            Vector3 worldAvoidance = avoidanceDirection.normalized;
            if (forceBridgeApproach && probedBlockingBridge != null)
            {
                worldAvoidance = SafeDirection(
                    probedBridgePoint - bossBounds.center,
                    worldAvoidance);
            }
            Vector3 avoidanceAxes =
                ModularBossSteeringPolicy.ToAimRelativeAxes(
                    worldAvoidance,
                    aim,
                    up);
            motion.SetInjectedControl(new RobocraftControlFrame
            {
                move = new Vector2(
                    Mathf.Clamp(avoidanceAxes.x * 1.25f, -1f, 1f),
                    Mathf.Clamp(avoidanceAxes.z, -0.45f, 1f)) *
                    mobilityScale,
                vertical = Mathf.Clamp(
                    avoidanceAxes.y * 1.35f,
                    -1f,
                    1f) * mobilityScale,
                roll = emergencyAssist
                    ? Mathf.Sin(Time.time * 1.7f) * 0.08f
                    : 0f,
                // RC3 braking is a position-hold mode: it intentionally
                // discards move/vertical translation commands. Local
                // avoidance must keep translating even when the chosen
                // escape direction is sideways or slightly backwards,
                // otherwise contact is refreshed forever and the Boss
                // hover-locks against the obstacle it is trying to avoid.
                braking = false,
                boost = forceBridgeApproach && !emergencyAssist,
                freeLook = false,
                aimForwardWorld = aim,
                hasAimOverride = true
            });
            return;
        }

        Vector3 planarVelocity = Vector3.ProjectOnPlane(body.velocity, up);
        bool braking = Mathf.Abs(rangeError) < 18f &&
                       planarVelocity.magnitude >
                       (emergencyAssist ? 8f : 15f);
        motion.SetInjectedControl(new RobocraftControlFrame
        {
            move = new Vector2(
                orbit * mobilityScale,
                forward * mobilityScale),
            vertical = vertical * mobilityScale,
            roll = emergencyAssist
                ? Mathf.Sin(Time.time * 1.7f) * 0.08f
                : 0f,
            braking = braking,
            boost = !emergencyAssist && rangeError > 85f,
            freeLook = false,
            aimForwardWorld = aim,
            hasAimOverride = true
        });
    }

    void TryFire()
    {
        if (WeaponsSuppressed || Time.time < nextShotAt ||
            Time.time < nextTargetSearchAt || liveWeapons.Count == 0)
            return;
        GridModuleView view = liveWeapons[
            weaponCursor++ % liveWeapons.Count];
        if (view == null || view.Record == null)
        {
            RebuildLiveModules();
            return;
        }
        NeoXBehaviorModule semantics =
            view.GetComponentInChildren<NeoXBehaviorModule>(true);
        Vector3 origin = semantics != null
            ? semantics.WorldMuzzlePosition
            : view.transform.position + view.transform.forward;
        Vector3 muzzleForward = semantics != null
            ? semantics.WorldMuzzleDirection
            : view.transform.forward;
        if (!TryResolveFocusedPlayerTarget(origin, out Collider target))
        {
            // Fully occluded players used to make every rendered frame scan
            // every player module. Match the awareness cadence while seeking
            // a newly exposed module and avoid cover-dependent GC spikes.
            nextTargetSearchAt = Time.time + 0.12f;
            return;
        }
        nextTargetSearchAt = 0f;
        Vector3 desired = target.bounds.center - origin;
        float distance = desired.magnitude;
        if (distance < 0.01f)
            return;
        desired /= distance;
        if (Vector3.Dot(muzzleForward, desired) < 0.82f)
            return;

        float baseInaccuracy = Mathf.Lerp(
            0.038f,
            0.012f,
            build.Profile.Tier / 5f);
        float inaccuracy = ModularBossCombatPolicy.ResolveWeaponSpread(
            baseInaccuracy,
            directLineBlocked,
            nearbyUrbanCover,
            distance);
        Vector3 direction = (
            desired +
            transform.right * UnityEngine.Random.Range(-inaccuracy, inaccuracy) +
            transform.up * UnityEngine.Random.Range(-inaccuracy, inaccuracy))
            .normalized;
        WeaponProfile profile = WeaponProfileLibrary.Resolve(view);
        float range = Mathf.Min(profile.range, 720f);
        WeaponDamageUtility.Trace(
            origin,
            direction,
            range,
            transform,
            gameObject,
            profile,
            visuals,
            out RaycastHit hit);
        AdoptFocusedPlayerModule(hit.collider);
        Vector3 end = hit.collider != null
            ? hit.point
            : origin + direction * range;
        visuals.SpawnMuzzle(
            origin,
            direction,
            new Color(1f, 0.24f, 0.08f),
            profile.muzzleEffect,
            view.transform);
        visuals.SpawnTracer(
            origin,
            end,
            new Color(1f, 0.28f, 0.1f),
            0.09f,
            profile.projectileEffect);
        nextShotAt = Time.time +
                     ModularBossCombatPolicy.ResolveBossShotCadence(
                         profile.shotsPerSecond,
                         build.Profile.Tier,
                         liveWeapons.Count);
    }

    bool TryResolveFocusedPlayerTarget(
        Vector3 origin,
        out Collider target)
    {
        target = null;
        if (IsValidFocusedPlayerTarget() &&
            HasClearWeaponLineOfFire(origin, focusedPlayerCollider))
        {
            target = focusedPlayerCollider;
            return true;
        }

        ClearFocusedPlayerTarget();
        if (playerGraph == null || !playerGraph.Active)
            return false;

        VehicleModuleDamageReceiver[] modules = playerGraph
            .GetComponentsInChildren<VehicleModuleDamageReceiver>(true);
        float bestIntegrityRatio = float.PositiveInfinity;
        float bestDistance = float.PositiveInfinity;
        for (int index = 0; index < modules.Length; index++)
        {
            VehicleModuleDamageReceiver candidate = modules[index];
            if (candidate == null || candidate.IsDestroyed ||
                candidate.StructureGraph != playerGraph ||
                !TryResolveTargetCollider(candidate, out Collider collider) ||
                !HasClearWeaponLineOfFire(origin, collider))
            {
                continue;
            }

            float integrityRatio = candidate.Integrity /
                                   Mathf.Max(1f, candidate.MaximumIntegrity);
            float distance = collider.bounds.SqrDistance(origin);
            bool healthierThanBest =
                integrityRatio > bestIntegrityRatio + 0.0001f;
            if (healthierThanBest ||
                (Mathf.Abs(integrityRatio - bestIntegrityRatio) <= 0.0001f &&
                 distance >= bestDistance))
            {
                continue;
            }

            focusedPlayerModule = candidate;
            focusedPlayerCollider = collider;
            bestIntegrityRatio = integrityRatio;
            bestDistance = distance;
        }

        target = focusedPlayerCollider;
        return target != null;
    }

    bool HasClearWeaponLineOfFire(Vector3 origin, Collider target)
    {
        if (target == null)
            return false;
        Vector3 toTarget = target.bounds.center - origin;
        float distance = toTarget.magnitude;
        if (distance <= 0.01f)
            return true;

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            toTarget / distance,
            WeaponSightHits,
            distance + 0.5f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        if (hitCount >= WeaponSightHits.Length)
            return false;

        Collider nearestCollider = null;
        float nearestDistance = float.PositiveInfinity;
        for (int index = 0; index < hitCount; index++)
        {
            Collider collider = WeaponSightHits[index].collider;
            if (collider == null || !collider.enabled ||
                collider.transform.IsChildOf(transform) ||
                WeaponSightHits[index].distance >= nearestDistance)
            {
                continue;
            }
            nearestCollider = collider;
            nearestDistance = WeaponSightHits[index].distance;
        }

        return IsPlayerCollider(nearestCollider);
    }

    bool IsPlayerCollider(Collider collider)
    {
        if (collider == null)
            return false;
        if (playerBody != null &&
            collider.transform.IsChildOf(playerBody.transform))
        {
            return true;
        }
        VehicleModuleDamageReceiver receiver = collider
            .GetComponentInParent<VehicleModuleDamageReceiver>();
        return receiver != null &&
               receiver.StructureGraph == playerGraph;
    }

    bool IsValidFocusedPlayerTarget()
    {
        return focusedPlayerModule != null &&
               focusedPlayerCollider != null &&
               !focusedPlayerModule.IsDestroyed &&
               focusedPlayerModule.StructureGraph == playerGraph &&
               focusedPlayerCollider.enabled &&
               focusedPlayerCollider.gameObject.activeInHierarchy &&
               !focusedPlayerCollider.isTrigger;
    }

    static bool TryResolveTargetCollider(
        VehicleModuleDamageReceiver module,
        out Collider target)
    {
        target = null;
        Collider[] colliders = module.GetComponentsInChildren<Collider>(true);
        for (int index = 0; index < colliders.Length; index++)
        {
            Collider candidate = colliders[index];
            if (candidate == null || !candidate.enabled ||
                candidate.isTrigger ||
                !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            target = candidate;
            return true;
        }
        return false;
    }

    void AdoptFocusedPlayerModule(Collider hitCollider)
    {
        if (hitCollider == null)
            return;
        VehicleModuleDamageReceiver hitModule = hitCollider
            .GetComponentInParent<VehicleModuleDamageReceiver>();
        if (hitModule == null || hitModule.IsDestroyed ||
            hitModule.StructureGraph != playerGraph)
        {
            return;
        }

        focusedPlayerModule = hitModule;
        focusedPlayerCollider = hitCollider;
    }

    void ClearFocusedPlayerTarget()
    {
        focusedPlayerModule = null;
        focusedPlayerCollider = null;
    }

    void HandleStructureChanged(VehicleStructureDelta delta)
    {
        if (delta == null || delta.RemovedRuntimeIds.Count == 0 ||
            build == null)
        {
            RebuildLiveModules();
            return;
        }

        var removed = new HashSet<string>(
            delta.RemovedRuntimeIds,
            StringComparer.Ordinal);
        liveWeapons.RemoveAll(view =>
            view == null || view.Record == null ||
            removed.Contains(view.Record.RuntimeId));
        foreach (string runtimeId in removed)
        {
            if (!build.ThrusterDirections.TryGetValue(
                    runtimeId,
                    out ModularBossThrusterDirection direction) ||
                !liveThrusters.TryGetValue(direction, out int count))
            {
                continue;
            }
            liveThrusters[direction] = Mathf.Max(0, count - 1);
        }
    }

    void RebuildLiveModules()
    {
        liveWeapons.Clear();
        liveThrusters.Clear();
        foreach (ModularBossThrusterDirection direction in DirectionValues)
            liveThrusters[direction] = 0;
        if (presenter == null || build == null)
            return;
        foreach (GridModuleView view in presenter.Views.Values)
        {
            if (view?.Record == null)
                continue;
            if (WeaponProfileLibrary.IsWeapon(view))
                liveWeapons.Add(view);
            if (build.ThrusterDirections.TryGetValue(
                    view.Record.RuntimeId,
                    out ModularBossThrusterDirection direction))
            {
                liveThrusters[direction]++;
            }
        }
    }

    void ApplyBossModuleScale()
    {
        ModularBossModuleScalePolicy.Apply(presenter);
    }

    bool EnsureSafeSpawn(Vector3 intendedPosition, Quaternion rotation)
    {
        if (structureGraph == null)
            return false;
        Vector3[] localOffsets =
        {
            Vector3.zero,
            Vector3.right * 48f,
            Vector3.left * 48f,
            Vector3.forward * 48f,
            Vector3.back * 48f,
            new Vector3(72f, 0f, 72f),
            new Vector3(-72f, 0f, 72f),
            new Vector3(72f, 0f, -72f),
            new Vector3(-72f, 0f, -72f)
        };
        float[] verticalOffsets = { 0f, 48f, 96f, 160f, 240f, 340f, 480f };
        // Keep the road center first and gain altitude before trying lateral
        // offsets. The old ordering jumped sideways into a building gap at
        // ground level before testing clear air above the selected road.
        for (int offsetIndex = 0;
             offsetIndex < localOffsets.Length;
             offsetIndex++)
        {
            for (int verticalIndex = 0;
                 verticalIndex < verticalOffsets.Length;
                 verticalIndex++)
            {
                Vector3 candidate = intendedPosition +
                                    Vector3.up *
                                    verticalOffsets[verticalIndex] +
                                    rotation * localOffsets[offsetIndex];
                transform.SetPositionAndRotation(candidate, rotation);
                Physics.SyncTransforms();
                if (!IsCurrentSpawnClear())
                    continue;
                bool adjusted = Vector3.Distance(
                    candidate,
                    intendedPosition) > 0.1f;
                spawnWasAdjusted |= adjusted;
                if (adjusted)
                {
                    Debug.Log(
                        $"Boss spawn moved to a clear flight volume by " +
                        $"{candidate - intendedPosition}.",
                        this);
                }
                return true;
            }
        }

        Bounds bounds = structureGraph.ResolveVisualBounds();
        transform.SetPositionAndRotation(
            intendedPosition +
            Vector3.up * (520f + bounds.extents.y),
            rotation);
        Physics.SyncTransforms();
        spawnWasAdjusted = true;
        Debug.LogWarning(
            "Boss spawn search exhausted nearby volumes; using the " +
            "high-altitude safety fallback.",
            this);
        return IsCurrentSpawnClear();
    }

    bool IsCurrentSpawnClear()
    {
        Bounds bounds = structureGraph.ResolveVisualBounds();
        Vector3 extents = bounds.extents + Vector3.one * 1.5f;
        int hitCount = Physics.OverlapBoxNonAlloc(
            bounds.center,
            extents,
            spawnOverlapBuffer,
            Quaternion.identity,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        if (hitCount >= spawnOverlapBuffer.Length)
            return false;
        for (int index = 0; index < hitCount; index++)
        {
            Collider collider = spawnOverlapBuffer[index];
            if (collider == null ||
                !collider.enabled ||
                collider.transform.IsChildOf(transform))
            {
                continue;
            }
            UrbanDestructibleBridge bridge =
                collider.GetComponentInParent<UrbanDestructibleBridge>();
            if (bridge != null && bridge.IsUrbanDestroyed)
                continue;
            return false;
        }
        return true;
    }

    public int LiveThrustersFor(ModularBossThrusterDirection direction) =>
        liveThrusters.TryGetValue(direction, out int count) ? count : 0;

    public int InitialThrustersFor(ModularBossThrusterDirection direction) =>
        build == null ? 0 : build.CountThrusters(direction);

    void HandleDestroyed()
    {
        prepared = false;
        combatActive = false;
        shield?.SetCombatActive(false);
        ClearFocusedPlayerTarget();
        motion?.ClearInjectedControl();
        motion?.EndFlight();
        flight?.ExitFlight();
        Destroyed?.Invoke();
    }

    void OnDestroy()
    {
        if (presenter != null)
        {
            presenter.SetRuntimeRemovalOptimization(false);
        }
        if (structureGraph != null)
        {
            structureGraph.SetModuleDamageFilter(null);
            structureGraph.StructureChanged -= HandleStructureChanged;
            structureGraph.Destroyed -= HandleDestroyed;
        }
        motion?.ClearInjectedControl();
        motion?.EndFlight();
    }
}
