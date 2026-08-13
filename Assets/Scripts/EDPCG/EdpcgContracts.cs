using System;
using UnityEngine;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.EDPCG
{
    public enum EdpcgEncounterPhase
    {
        Preview,
        Engage,
        Peak,
        Recover
    }

    public enum EdpcgEnemyResolutionReason
    {
        None,
        KilledByPlayer,
        PlayerCausedEnvironment,
        SelfDetonated,
        NavigationRecovery,
        TechnicalFinalClear
    }

    public enum EdpcgRosterState
    {
        Unspawned,
        Queued,
        SpawnRecovery,
        Active,
        NavigationRecovery,
        Resolved
    }

    public enum EdpcgPathIntent
    {
        Ingress,
        FormUp,
        Probe,
        Intercept,
        CommitAttackRun,
        BreakAway,
        MaskedFlank,
        RangedPerch,
        Suppress,
        BreakLineOfSight,
        MaskedHold,
        BreakContact,
        Regroup,
        Withdraw,
        NavigationRecovery,
        ReacquirePlayer,
        EnvironmentalTrapCommit
    }

    public enum EdpcgEnemyTacticalState
    {
        Ingress,
        Forming,
        Positioning,
        Threatening,
        Telegraphing,
        Attacking,
        Disengaging,
        Regrouping,
        Withdrawing,
        NavigationRecovery,
        Resolved,
        Reengaging,
        EnvironmentalTrapCommit
    }

    public enum EdpcgWeaponPermission
    {
        Safe,
        TelegraphOnly,
        Live
    }

    public enum EdpcgTacticalAreaKind
    {
        None,
        SpawnSafeAirspace,
        CentralManeuverDistrict,
        HighRiseOcclusionChain,
        ExposedFireShortcut,
        MagneticCourtyard,
        [Obsolete("恢复庭院已改为三面磁场战斗陷阱。")]
        RepairCourtyard = MagneticCourtyard,
        LowMidVerticalTransition
    }

    public enum EdpcgTacticalAreaState
    {
        Outside,
        EnterPending,
        Active,
        ExitPending,
        Cooldown
    }

    public enum EdpcgLiveApplyPolicy
    {
        ApplyNow,
        ApplyAtSafeBoundary,
        RequiresRestart
    }

    public enum EdpcgIntegrationMode
    {
        Legacy = 0,
        ObserveOnly = 1,
        TacticalAssignments = 2,
        PressureV3Control = 3
    }

    public enum EdpcgRuntimeChangeState
    {
        Draft,
        Pending,
        Applied,
        Reverted,
        Cancelled
    }

    public enum EdpcgTelemetryEventKind
    {
        SessionStarted,
        SessionEnded,
        PhaseChanged,
        SpawnQueued,
        Spawned,
        SpawnRecovery,
        AttackTokenAcquired,
        AttackTokenReleased,
        StateChanged,
        PathRequested,
        PathFailed,
        ReservationCreated,
        ReservationReleased,
        AreaEntered,
        AreaExited,
        Damage,
        Collision,
        EnemyResolved,
        NavigationRecovery,
        TuningChange,
        Bookmark,
        ValidationError
    }

    [Serializable]
    public sealed class EdpcgRosterMember
    {
        public string rosterMemberId = string.Empty;
        public HordeEnemyRole role;
        public EdpcgRosterState state;
        public EdpcgEnemyResolutionReason resolutionReason;
        public int rosterIndex;
        public int spawnAttempts;
        public float healthRatio = 1f;
        public bool environmentalPursuer;
        public bool creditedKill;
        public bool rewardGranted;

        public bool IsResolved => state == EdpcgRosterState.Resolved;
    }

    public readonly struct EdpcgRosterAssignment
    {
        public readonly string RosterMemberId;
        public readonly HordeEnemyRole Role;
        public readonly int RosterIndex;
        public readonly bool EnvironmentalPursuer;

        public EdpcgRosterAssignment(
            string rosterMemberId,
            HordeEnemyRole role,
            int rosterIndex,
            bool environmentalPursuer = false)
        {
            RosterMemberId = rosterMemberId ?? string.Empty;
            Role = role;
            RosterIndex = rosterIndex;
            EnvironmentalPursuer = environmentalPursuer;
        }
    }

    [Serializable]
    public sealed class EdpcgPressureSample
    {
        public float missionTime;
        public EdpcgEncounterPhase phase;
        public float targetPressureMinimum;
        public float targetPressureMaximum;
        public float actualPressure;
        public float forecastPressure4Seconds;
        public float forecastPressure8Seconds;
        public float enemyThreatPressure;
        public float measuredEnvironmentPressure;
        public float navigationPressure;
        public float suicideNavigationPressure;
        public float rangedNavigationPressure;
        public float committedNavigationPressure;
        public float environmentalPursuitPressure;
        public float playerStrain;
        public float playerDamageAssist;
        public float observedCombatPressure;
        public float observedFirePressure;
        public float observedInterceptPressure;
        public float observedDisplacementPressure;
        public float observedNetEnvironmentPressure;
        public float observedPlayerRisk;
        public float systemHealthIssueRatio;
        public int pressureDirectionCount;
        public int pressureDirectionMask;
        public bool environmentObservationAvailable;
        public int rosterCount;
        public int unspawnedCount;
        public int queuedCount;
        public int spawnRecoveryCount;
        public int activeCount;
        public int engagementCount;
        public int attackTokensUsed;
        public int suicideCommitCount;
        public int rangedFireLaneCount;
        public int pursuingThreatCount;
        public int closeApproachThreatCount;
        public int navigationRecoveryCount;
        public int environmentalPursuitCount;
        public int pressureAssistLevel;
        public int pressureBrakeLevel;
        public int resolvedCount;
        public string activeTacticalAreaId = string.Empty;
        public EdpcgTacticalAreaKind activeTacticalAreaKind;

        public EdpcgPressureSample Copy()
        {
            return (EdpcgPressureSample)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class EdpcgTelemetryEvent
    {
        public float missionTime;
        public EdpcgTelemetryEventKind kind;
        public string stableEventId = string.Empty;
        public string rosterMemberId = string.Empty;
        public string squadId = string.Empty;
        public string areaId = string.Empty;
        public string routeId = string.Empty;
        public string reasonCode = string.Empty;
        public float pressureDelta;
        public Vector3 worldPosition;
    }

    [Serializable]
    public sealed class EdpcgRuntimeChange
    {
        public string changeId = string.Empty;
        public string fieldPath = string.Empty;
        public string previousSerializedValue = string.Empty;
        public string candidateSerializedValue = string.Empty;
        public float submittedAt;
        public float appliedAt = -1f;
        public EdpcgLiveApplyPolicy applyPolicy;
        public EdpcgRuntimeChangeState state;
        public string safeBoundaryReason = string.Empty;
    }

    [Serializable]
    public sealed class EdpcgRouteReservation
    {
        public string reservationId = string.Empty;
        public string ownerRosterMemberId = string.Empty;
        public string routeId = string.Empty;
        public float enterAt;
        public float exitAt;
        public float expiresAt;
        public int priority;
        public int direction;
        public int waypointIndex;
        public bool hasTargetProgress;
        public int targetSegmentIndex = -1;
        public float targetSegmentT;
        public Vector3 targetWorldPosition;
        public string targetStableId = string.Empty;
    }

    [Serializable]
    public sealed class EdpcgExportManifest
    {
        public string generatedAtUtc = string.Empty;
        public string profileVersion = string.Empty;
        public string formulaVersion = string.Empty;
        public string buildVersion = string.Empty;
        public string missionId = string.Empty;
        public int planetDifficultyTier;
        public int citySeed;
        public int rosterSeed;
        public float rangeStart;
        public float rangeEnd;
        public float sampleRate;
        public string[] files = Array.Empty<string>();
        public EdpcgRuntimeChange[] tuningChanges =
            Array.Empty<EdpcgRuntimeChange>();
    }
}
