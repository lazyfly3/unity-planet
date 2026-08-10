using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using ModularAssembly;
using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.EDPCG
{
    /// <summary>
    /// Non-Boss EDPCG session state. It owns the fixed roster, pressure model,
    /// city semantics and live tuning transaction log. Enemy simulation stays
    /// in HordeCombatDirector/HordeEnemyVehicle so there is one combat owner.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EdpcgEncounterRuntime : MonoBehaviour
    {
        sealed class EnvironmentalCommitment
        {
            public EdpcgRuntimeRoute route;
            public string reservationId = string.Empty;
            public Rigidbody body;
        }

        const float TelemetryInterval = 0.1f;
        const float PressureEpsilon = 0.001f;

        readonly List<EdpcgRosterMember> roster =
            new List<EdpcgRosterMember>(64);
        readonly Dictionary<string, EdpcgRosterMember> memberById =
            new Dictionary<string, EdpcgRosterMember>(StringComparer.Ordinal);
        readonly Dictionary<string, object> originalValues =
            new Dictionary<string, object>(StringComparer.Ordinal);
        readonly Dictionary<string, EnvironmentalCommitment>
            environmentalCommitments =
                new Dictionary<string, EnvironmentalCommitment>(
                    StringComparer.Ordinal);
        readonly Dictionary<string, EdpcgRuntimeRoute>
            environmentalFailureBlocks =
                new Dictionary<string, EdpcgRuntimeRoute>(
                    StringComparer.Ordinal);
        readonly List<string> environmentalCleanupIds =
            new List<string>(16);

        HordeCombatDirector director;
        Rigidbody playerBody;
        VehicleStructureGraph playerGraph;
        EdpcgDifficultyProfile profile;
        EdpcgTierSettings settings;
        EdpcgTierSettings baselineSettings;
        EdpcgCityTacticalRuntimeMap tacticalMap;
        EdpcgRouteReservationService reservations;
        EdpcgTelemetryRecorder recorder;
        EdpcgPressureSample currentSample = new EdpcgPressureSample();
        EdpcgEncounterPhase phase;
        EdpcgRuntimeArea areaCandidate;
        EdpcgRuntimeArea activeArea;
        float areaCandidateSince;
        float areaExitedSince = -1f;
        float elapsed;
        float nextTelemetryAt;
        float smoothedPressure;
        float previousRawPressure;
        float pressureVelocity;
        int activeCount;
        int engagementCount;
        int attackTokensUsed;
        int suicideCommitCount;
        int rangedFireLaneCount;
        int navigationRecoveryCount;
        int activeThreatCost;
        int changeSequence;
        int rosterSeed;
        int citySeed;
        string missionId = string.Empty;
        bool configured;
        bool running;

        public EdpcgDifficultyProfile Profile => profile;
        public EdpcgTierSettings Settings => settings;
        public EdpcgTierSettings BaselineSettings => baselineSettings;
        public EdpcgCityTacticalRuntimeMap TacticalMap => tacticalMap;
        public EdpcgRouteReservationService Reservations => reservations;
        public EdpcgTelemetryRecorder Recorder => recorder;
        public IReadOnlyList<EdpcgRosterMember> Roster => roster;
        public EdpcgPressureSample CurrentSample => currentSample;
        public EdpcgEncounterPhase Phase => phase;
        public float Elapsed => elapsed;
        public bool IsConfigured => configured;
        public bool IsRunning => running;
        public string MissionId => missionId;
        public int CitySeed => citySeed;
        public int RosterSeed => rosterSeed;
        public int RosterCount => roster.Count;
        public int CreditedKills { get; private set; }
        public int ResolvedCount { get; private set; }
        public int UnresolvedCount => Mathf.Max(0, roster.Count - ResolvedCount);
        public int EnvironmentalPursuitCount =>
            environmentalCommitments.Count;
        public bool IsRosterResolved => roster.Count > 0 &&
                                        ResolvedCount >= roster.Count;
        public bool CompletionQuotaMet => settings != null &&
                                          CreditedKills >=
                                          settings.requiredCreditedKills;
        // The credited-kill quota drives pacing and scoring, but it must not
        // soft-lock a mission after every fixed-roster member is already gone.
        public bool IsEncounterResolved => IsRosterResolved;

        public int PopulationCap
        {
            get
            {
                if (settings == null)
                    return 1;
                float multiplier = phase == EdpcgEncounterPhase.Preview
                    ? 0.45f
                    : phase == EdpcgEncounterPhase.Engage
                        ? 0.82f
                        : phase == EdpcgEncounterPhase.Peak ? 1f : 0.58f;
                int cap = Mathf.CeilToInt(settings.populationCap * multiplier);
                if (currentSample.actualPressure <
                    currentSample.targetPressureMinimum - 0.08f)
                {
                    cap++;
                }
                else if (currentSample.actualPressure >
                         currentSample.targetPressureMaximum + 0.04f)
                {
                    cap -= 2;
                }
                return Mathf.Clamp(cap, 1, settings.populationCap);
            }
        }

        public int EngagementCap
        {
            get
            {
                if (settings == null)
                    return 1;
                int cap = Mathf.Min(settings.engagementCap, PopulationCap);
                if (phase == EdpcgEncounterPhase.Preview ||
                    phase == EdpcgEncounterPhase.Recover)
                {
                    cap = Mathf.CeilToInt(cap * 0.55f);
                }
                return Mathf.Max(1, cap);
            }
        }

        public int AttackTokenCap
        {
            get
            {
                if (settings == null)
                    return 1;
                int cap = settings.attackTokenCap;
                if (phase == EdpcgEncounterPhase.Preview ||
                    phase == EdpcgEncounterPhase.Recover)
                {
                    cap = 1;
                }
                if (currentSample.actualPressure >
                    currentSample.targetPressureMaximum)
                {
                    cap--;
                }
                return Mathf.Clamp(cap, 1, settings.attackTokenCap);
            }
        }

        public int StrategyLevel
        {
            get
            {
                if (settings == null)
                    return 0;
                int level = phase == EdpcgEncounterPhase.Preview
                    ? 0
                    : phase == EdpcgEncounterPhase.Engage
                        ? 1
                        : phase == EdpcgEncounterPhase.Peak ? 2 : 0;
                if (currentSample.actualPressure <
                    currentSample.targetPressureMinimum - 0.06f)
                {
                    level++;
                }
                if (currentSample.actualPressure >
                    currentSample.targetPressureMaximum)
                {
                    level--;
                }
                return Mathf.Clamp(level, 0, settings.maximumStrategyLevel);
            }
        }

        public float SpawnInterval
        {
            get
            {
                if (settings == null)
                    return 4f;
                float multiplier = phase == EdpcgEncounterPhase.Recover
                    ? 1.75f
                    : phase == EdpcgEncounterPhase.Preview ? 1.35f : 1f;
                if (currentSample.actualPressure >
                    currentSample.targetPressureMaximum)
                {
                    multiplier *= 1.35f;
                }
                else if (currentSample.actualPressure <
                         currentSample.targetPressureMinimum)
                {
                    multiplier *= 0.82f;
                }
                return Mathf.Max(0.25f,
                    settings.spawnIntervalSeconds * multiplier);
            }
        }

        public void Configure(
            HordeCombatDirector combatDirector,
            Rigidbody targetPlayerBody,
            VehicleStructureGraph targetPlayerGraph,
            FinitePlanetUrbanCombatRuntime urban,
            string targetMissionId,
            int zeroBasedPlanetTier,
            int deterministicSeed,
            EdpcgDifficultyProfile difficultyProfile = null)
        {
            director = combatDirector;
            playerBody = targetPlayerBody;
            playerGraph = targetPlayerGraph;
            profile = difficultyProfile ??
                      EdpcgDifficultyProfile.LoadOrCreateMemoryDefault();
            settings = profile.Resolve(zeroBasedPlanetTier);
            baselineSettings = settings.ValidatedCopy();
            missionId = targetMissionId ?? string.Empty;
            citySeed = deterministicSeed;
            rosterSeed = deterministicSeed ^ unchecked((int)0x45D9F3B);
            tacticalMap = EdpcgCityTacticalRuntimeMap.Build(urban);
            reservations = new EdpcgRouteReservationService(tacticalMap);
            recorder = new EdpcgTelemetryRecorder();
            configured = director != null && playerBody != null &&
                         playerGraph != null;
            running = false;
        }

        public void BeginSession()
        {
            if (!configured)
                throw new InvalidOperationException(
                    "EDPCG must be configured before the session starts.");
            BuildRoster();
            recorder.Clear();
            reservations.Clear();
            ClearEnvironmentalTrapCommitments();
            originalValues.Clear();
            elapsed = 0f;
            nextTelemetryAt = 0f;
            smoothedPressure = 0f;
            previousRawPressure = 0f;
            pressureVelocity = 0f;
            activeArea = null;
            areaCandidate = null;
            areaCandidateSince = 0f;
            areaExitedSince = -1f;
            phase = EdpcgEncounterPhase.Preview;
            currentSample = new EdpcgPressureSample
            {
                rosterCount = roster.Count,
                unspawnedCount = roster.Count,
                phase = phase
            };
            settings.ResolveTargetBand(
                phase,
                out currentSample.targetPressureMinimum,
                out currentSample.targetPressureMaximum);
            running = true;
            EdpcgRuntimeRegistry.Register(this);
            recorder.RecordEvent(0f, EdpcgTelemetryEventKind.SessionStarted,
                "fixed-roster:" + roster.Count.ToString(CultureInfo.InvariantCulture));
        }

        public void EndSession()
        {
            if (running && recorder != null)
            {
                recorder.RecordEvent(
                    elapsed,
                    EdpcgTelemetryEventKind.SessionEnded,
                    IsEncounterResolved ? "resolved" : "interrupted");
            }
            running = false;
            ClearEnvironmentalTrapCommitments();
            reservations?.Clear();
            EdpcgRuntimeRegistry.Unregister(this);
        }

        void ClearEnvironmentalTrapCommitments()
        {
            foreach (KeyValuePair<string, EnvironmentalCommitment> pair in
                     environmentalCommitments)
            {
                EnvironmentalCommitment commitment = pair.Value;
                commitment?.route?.EnvironmentalTrap?.field?
                    .NotifyPursuitReleased(commitment.body);
            }
            environmentalCommitments.Clear();
            environmentalFailureBlocks.Clear();
            environmentalCleanupIds.Clear();
        }

        public void Tick(float deltaTime)
        {
            if (!running || settings == null)
                return;
            float safeDelta = Mathf.Max(0f, deltaTime);
            elapsed += safeDelta;
            EdpcgEncounterPhase nextPhase = settings.ResolvePhase(
                elapsed,
                out _,
                out _);
            if (nextPhase != phase)
            {
                phase = nextPhase;
                recorder.RecordEvent(
                    elapsed,
                    EdpcgTelemetryEventKind.PhaseChanged,
                    phase.ToString());
                ApplyPendingChangesAtSafeBoundary("phase-change");
            }
            reservations.ReleaseExpired(Time.time);
            ReconcileEnvironmentalTrapState();
            UpdateAreaState();
            UpdatePressure(safeDelta);
            if (elapsed + PressureEpsilon >= nextTelemetryAt)
            {
                recorder.RecordSample(currentSample);
                nextTelemetryAt = elapsed + TelemetryInterval;
            }
        }

        public void UpdateDirectorSnapshot(
            int currentActiveCount,
            int currentEngagementCount,
            int currentAttackTokens,
            int currentSuicideCommits,
            int currentRangedFireLanes,
            int currentNavigationRecoveries,
            int currentActiveThreatCost)
        {
            activeCount = Mathf.Max(0, currentActiveCount);
            engagementCount = Mathf.Max(0, currentEngagementCount);
            attackTokensUsed = Mathf.Max(0, currentAttackTokens);
            suicideCommitCount = Mathf.Max(0, currentSuicideCommits);
            rangedFireLaneCount = Mathf.Max(0, currentRangedFireLanes);
            navigationRecoveryCount = Mathf.Max(
                0,
                currentNavigationRecoveries);
            activeThreatCost = Mathf.Max(0, currentActiveThreatCost);
        }

        public bool TryReserveNextRosterMember(
            HordeEnemyRole preferredRole,
            out EdpcgRosterAssignment assignment)
        {
            assignment = default(EdpcgRosterAssignment);
            if (!running)
                return false;
            bool trapOpportunity = preferredRole ==
                                   HordeEnemyRole.Interceptor &&
                                   playerBody != null && tacticalMap != null &&
                                   tacticalMap.TrySelectEnvironmentalTrapRoute(
                                       playerBody.worldCenterOfMass,
                                       playerBody.worldCenterOfMass,
                                       out _);
            EdpcgRosterMember selected = trapOpportunity
                ? FindUnspawnedEnvironmentalPursuer()
                : null;
            selected = selected ?? FindUnspawned(preferredRole) ??
                                         FindUnspawnedAnyRole();
            if (selected == null)
                return false;
            selected.state = EdpcgRosterState.Queued;
            assignment = new EdpcgRosterAssignment(
                selected.rosterMemberId,
                selected.role,
                selected.rosterIndex,
                selected.environmentalPursuer);
            recorder.RecordEvent(
                elapsed,
                EdpcgTelemetryEventKind.SpawnQueued,
                selected.role.ToString(),
                selected.rosterMemberId);
            return true;
        }

        public bool TryGetAssignment(
            string rosterMemberId,
            out EdpcgRosterAssignment assignment)
        {
            assignment = default(EdpcgRosterAssignment);
            if (!TryGetMember(rosterMemberId, out EdpcgRosterMember member) ||
                member.IsResolved)
            {
                return false;
            }
            assignment = new EdpcgRosterAssignment(
                member.rosterMemberId,
                member.role,
                member.rosterIndex,
                member.environmentalPursuer);
            return true;
        }

        public bool TryAcquireOrMaintainEnvironmentalTrap(
            HordeEnemyVehicle enemy,
            out Vector3 destination)
        {
            destination = Vector3.zero;
            if (!running || enemy == null || !enemy.IsCombatCapable ||
                enemy.Role == HordeEnemyRole.Gunship || playerBody == null ||
                tacticalMap == null ||
                string.IsNullOrEmpty(enemy.RosterMemberId))
            {
                return false;
            }

            if (environmentalCommitments.TryGetValue(
                    enemy.RosterMemberId,
                    out EnvironmentalCommitment existing))
            {
                UrbanEnvironmentalPursuitRouteDescriptor descriptor =
                    existing.route?.EnvironmentalTrap;
                if (descriptor != null &&
                    descriptor.IsAvailableForCommit(
                        playerBody.worldCenterOfMass) &&
                    reservations.Renew(
                        existing.reservationId,
                        Time.time,
                        Mathf.Max(
                            settings.reservationLeaseSeconds,
                            existing.route.EstimatedTravelSeconds + 3f)))
                {
                    destination = existing.route.Points[
                        existing.route.Points.Length - 1];
                    descriptor.field.NotifyPursuitAssigned(
                        enemy.Body,
                        existing.route.EstimatedTravelSeconds);
                    return true;
                }
                ReleaseEnvironmentalTrapCommitment(enemy, "opportunity-ended");
            }

            // Two failed graph queries mean this aircraft cannot currently
            // traverse the authored trap approach.  Keep it on ordinary AI
            // until the player actually leaves that opportunity (or the
            // field cycles unavailable), instead of releasing and reacquiring
            // the same route every frame.
            if (IsEnvironmentalTrapRetryBlocked(enemy.RosterMemberId))
                return false;

            if (director != null &&
                director.HasHigherPriorityEnvironmentalCandidate(enemy))
            {
                return false;
            }
            int effectiveEngagements = Mathf.Max(
                engagementCount,
                attackTokensUsed + environmentalCommitments.Count);
            if (effectiveEngagements >= EngagementCap ||
                currentSample.actualPressure >= settings.hardPressureLimit ||
                currentSample.forecastPressure4Seconds >=
                settings.hardPressureLimit ||
                !tacticalMap.TrySelectEnvironmentalTrapRoute(
                    playerBody.worldCenterOfMass,
                    enemy.BodyPosition,
                    out EdpcgRuntimeRoute route))
            {
                return false;
            }

            float travelSeconds = Vector3.Distance(
                enemy.BodyPosition,
                route.Points[0]) /
                Mathf.Max(18f, enemy.Profile.maximumSpeed) +
                route.EstimatedTravelSeconds;
            int priority = enemy.IsEnvironmentalPursuer
                ? 3
                : enemy.Role == HordeEnemyRole.Interceptor ? 2 : 1;
            if (!reservations.TryReserve(
                    enemy.RosterMemberId,
                    route.StableId,
                    Time.time,
                    travelSeconds,
                    Mathf.Max(
                        settings.reservationLeaseSeconds,
                        travelSeconds + 3f),
                    priority,
                    1,
                    out string reservationId))
            {
                return false;
            }

            var commitment = new EnvironmentalCommitment
            {
                route = route,
                reservationId = reservationId,
                body = enemy.Body
            };
            environmentalCommitments.Add(
                enemy.RosterMemberId,
                commitment);
            environmentalFailureBlocks.Remove(enemy.RosterMemberId);
            route.EnvironmentalTrap.field.NotifyPursuitAssigned(
                enemy.Body,
                travelSeconds);
            recorder.RecordEvent(
                elapsed,
                EdpcgTelemetryEventKind.ReservationCreated,
                "environmental-trap-commit",
                enemy.RosterMemberId,
                enemy.BodyPosition,
                0f,
                string.Empty,
                route.StableId);
            destination = route.Points[route.Points.Length - 1];
            return true;
        }

        public bool IsEnvironmentalTrapCommitted(string rosterMemberId)
        {
            return !string.IsNullOrEmpty(rosterMemberId) &&
                   environmentalCommitments.ContainsKey(rosterMemberId);
        }

        public bool CanAttemptEnvironmentalTrap(HordeEnemyVehicle enemy)
        {
            if (!running || enemy == null || !enemy.IsCombatCapable ||
                enemy.Role == HordeEnemyRole.Gunship || playerBody == null ||
                tacticalMap == null)
            {
                return false;
            }
            if (IsEnvironmentalTrapCommitted(enemy.RosterMemberId))
                return true;
            if (IsEnvironmentalTrapRetryBlocked(enemy.RosterMemberId))
                return false;
            return tacticalMap.TrySelectEnvironmentalTrapRoute(
                playerBody.worldCenterOfMass,
                enemy.BodyPosition,
                out _);
        }

        public void ReleaseEnvironmentalTrapCommitment(
            HordeEnemyVehicle enemy,
            string reason)
        {
            if (enemy == null)
                return;
            ReleaseEnvironmentalTrapCommitment(
                enemy.RosterMemberId,
                reason,
                enemy.BodyPosition);
        }

        void ReleaseEnvironmentalTrapCommitment(
            string rosterMemberId,
            string reason,
            Vector3 position)
        {
            if (string.IsNullOrEmpty(rosterMemberId) ||
                !environmentalCommitments.TryGetValue(
                    rosterMemberId,
                    out EnvironmentalCommitment commitment))
                return;
            string releaseReason = reason ??
                                   "environmental-trap-release";
            if (string.Equals(
                    releaseReason,
                    "route-not-found",
                    StringComparison.Ordinal) &&
                commitment.route != null)
            {
                environmentalFailureBlocks[rosterMemberId] =
                    commitment.route;
            }
            environmentalCommitments.Remove(rosterMemberId);
            reservations.Release(commitment.reservationId);
            commitment.route?.EnvironmentalTrap?.field?.NotifyPursuitReleased(
                commitment.body);
            recorder.RecordEvent(
                elapsed,
                EdpcgTelemetryEventKind.ReservationReleased,
                releaseReason,
                rosterMemberId,
                position,
                0f,
                string.Empty,
                commitment.route?.StableId ?? string.Empty);
        }

        bool IsEnvironmentalTrapRetryBlocked(string rosterMemberId)
        {
            if (string.IsNullOrEmpty(rosterMemberId) ||
                !environmentalFailureBlocks.TryGetValue(
                    rosterMemberId,
                    out EdpcgRuntimeRoute blockedRoute))
            {
                return false;
            }
            UrbanEnvironmentalPursuitRouteDescriptor descriptor =
                blockedRoute?.EnvironmentalTrap;
            bool opportunityStillOpen = descriptor != null &&
                playerBody != null &&
                descriptor.IsAvailableForCommit(
                    playerBody.worldCenterOfMass);
            if (opportunityStillOpen)
                return true;
            environmentalFailureBlocks.Remove(rosterMemberId);
            return false;
        }

        void ReconcileEnvironmentalTrapState()
        {
            environmentalCleanupIds.Clear();
            foreach (KeyValuePair<string, EnvironmentalCommitment> pair in
                     environmentalCommitments)
            {
                EnvironmentalCommitment commitment = pair.Value;
                if (commitment == null || reservations == null ||
                    !reservations.HasReservation(
                        commitment.reservationId))
                {
                    environmentalCleanupIds.Add(pair.Key);
                }
            }
            for (int index = 0;
                 index < environmentalCleanupIds.Count;
                 index++)
            {
                string rosterMemberId = environmentalCleanupIds[index];
                ReleaseEnvironmentalTrapCommitment(
                    rosterMemberId,
                    "lease-expired",
                    Vector3.zero);
            }

            environmentalCleanupIds.Clear();
            foreach (KeyValuePair<string, EdpcgRuntimeRoute> pair in
                     environmentalFailureBlocks)
            {
                UrbanEnvironmentalPursuitRouteDescriptor descriptor =
                    pair.Value?.EnvironmentalTrap;
                if (descriptor == null || playerBody == null ||
                    !descriptor.IsAvailableForCommit(
                        playerBody.worldCenterOfMass))
                {
                    environmentalCleanupIds.Add(pair.Key);
                }
            }
            for (int index = 0;
                 index < environmentalCleanupIds.Count;
                 index++)
            {
                environmentalFailureBlocks.Remove(
                    environmentalCleanupIds[index]);
            }
            environmentalCleanupIds.Clear();
        }

        public void MarkSpawnRecovery(string rosterMemberId, int attempts)
        {
            if (!TryGetMember(rosterMemberId, out EdpcgRosterMember member) ||
                member.IsResolved)
            {
                return;
            }
            member.state = EdpcgRosterState.SpawnRecovery;
            member.spawnAttempts = Mathf.Max(member.spawnAttempts, attempts);
            recorder.RecordEvent(
                elapsed,
                EdpcgTelemetryEventKind.SpawnRecovery,
                "spawn-position-unavailable",
                rosterMemberId);
        }

        public void MarkQueued(string rosterMemberId)
        {
            if (TryGetMember(rosterMemberId, out EdpcgRosterMember member) &&
                !member.IsResolved)
            {
                member.state = EdpcgRosterState.Queued;
            }
        }

        public void MarkSpawned(string rosterMemberId, Vector3 position)
        {
            if (!TryGetMember(rosterMemberId, out EdpcgRosterMember member) ||
                member.IsResolved)
            {
                return;
            }
            member.state = EdpcgRosterState.Active;
            recorder.RecordEvent(
                elapsed,
                EdpcgTelemetryEventKind.Spawned,
                member.role.ToString(),
                rosterMemberId,
                position);
        }

        public bool MarkNavigationRecovery(
            string rosterMemberId,
            Vector3 position,
            string reason)
        {
            if (!TryGetMember(rosterMemberId, out EdpcgRosterMember member) ||
                member.IsResolved)
            {
                return false;
            }
            member.state = EdpcgRosterState.NavigationRecovery;
            environmentalFailureBlocks.Remove(rosterMemberId);
            ReleaseEnvironmentalTrapCommitment(
                rosterMemberId,
                "navigation-recovery",
                position);
            reservations.ReleaseOwner(rosterMemberId);
            recorder.RecordEvent(
                elapsed,
                EdpcgTelemetryEventKind.NavigationRecovery,
                reason,
                rosterMemberId,
                position);
            return true;
        }

        public bool ResolveMember(
            string rosterMemberId,
            EdpcgEnemyResolutionReason reason,
            Vector3 position,
            out bool creditedKill)
        {
            creditedKill = false;
            if (!TryGetMember(rosterMemberId, out EdpcgRosterMember member) ||
                member.IsResolved)
            {
                return false;
            }
            member.state = EdpcgRosterState.Resolved;
            member.resolutionReason = reason;
            member.creditedKill = reason ==
                                  EdpcgEnemyResolutionReason.KilledByPlayer ||
                                  reason == EdpcgEnemyResolutionReason.
                                      PlayerCausedEnvironment;
            member.rewardGranted = member.creditedKill;
            creditedKill = member.creditedKill;
            ResolvedCount++;
            if (creditedKill)
                CreditedKills++;
            environmentalFailureBlocks.Remove(rosterMemberId);
            ReleaseEnvironmentalTrapCommitment(
                rosterMemberId,
                "enemy-resolved",
                position);
            reservations.ReleaseOwner(rosterMemberId);
            recorder.RecordEvent(
                elapsed,
                EdpcgTelemetryEventKind.EnemyResolved,
                reason.ToString(),
                rosterMemberId,
                position);
            return true;
        }

        public bool TryResolveTacticalDestination(
            HordeEnemyVehicle enemy,
            EdpcgPathIntent intent,
            out Vector3 destination,
            out string areaId,
            out string routeId,
            out string reservationId)
        {
            destination = Vector3.zero;
            areaId = string.Empty;
            routeId = string.Empty;
            reservationId = string.Empty;
            if (enemy == null || playerBody == null || tacticalMap == null ||
                string.IsNullOrEmpty(enemy.RosterMemberId))
            {
                return false;
            }
            if (!tacticalMap.TrySelectRoleDestination(
                    enemy.Role,
                    enemy.RosterIndex,
                    playerBody.worldCenterOfMass,
                    intent,
                    out destination,
                    out areaId,
                    out routeId))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(routeId) &&
                tacticalMap.TryGetRoute(routeId, out EdpcgRuntimeRoute route))
            {
                reservations.ReleaseOwner(enemy.RosterMemberId);
                if (reservations.TryReserve(
                        enemy.RosterMemberId,
                        routeId,
                        Time.time,
                        route.EstimatedTravelSeconds,
                        settings.reservationLeaseSeconds,
                        intent == EdpcgPathIntent.BreakContact ? 2 : 1,
                        1,
                        out reservationId))
                {
                    recorder.RecordEvent(
                        elapsed,
                        EdpcgTelemetryEventKind.ReservationCreated,
                        intent.ToString(),
                        enemy.RosterMemberId,
                        enemy.BodyPosition,
                        0f,
                        areaId,
                        routeId);
                }
                else if (tacticalMap.TryGetRoute(
                             route.FallbackRouteId,
                             out EdpcgRuntimeRoute fallback) &&
                         reservations.TryReserve(
                             enemy.RosterMemberId,
                             fallback.StableId,
                             Time.time,
                             fallback.EstimatedTravelSeconds,
                             settings.reservationLeaseSeconds,
                             1,
                             1,
                             out reservationId))
                {
                    routeId = fallback.StableId;
                }
                else
                {
                    routeId = string.Empty;
                }
            }
            recorder.RecordEvent(
                elapsed,
                EdpcgTelemetryEventKind.PathRequested,
                intent.ToString(),
                enemy.RosterMemberId,
                enemy.BodyPosition,
                0f,
                areaId,
                routeId);
            return true;
        }

        public bool TryGetReservedRouteWaypoints(
            string rosterMemberId,
            Vector3 start,
            List<Vector3> output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            output.Clear();
            if (string.IsNullOrEmpty(rosterMemberId) ||
                reservations == null || tacticalMap == null)
            {
                return false;
            }
            EdpcgRouteReservation selected = null;
            IReadOnlyList<EdpcgRouteReservation> activeReservations =
                reservations.Reservations;
            for (int index = activeReservations.Count - 1; index >= 0; index--)
            {
                if (string.Equals(
                        activeReservations[index].ownerRosterMemberId,
                        rosterMemberId,
                        StringComparison.Ordinal))
                {
                    selected = activeReservations[index];
                    break;
                }
            }
            if (selected == null ||
                !tacticalMap.TryGetRoute(
                    selected.routeId,
                    out EdpcgRuntimeRoute route) ||
                route.Points == null || route.Points.Length < 2)
            {
                return false;
            }
            if (!route.AllowReverse)
            {
                int waypointIndex = Mathf.Clamp(
                    selected.waypointIndex,
                    0,
                    route.Points.Length - 1);
                while (waypointIndex < route.Points.Length - 1 &&
                       Vector3.Distance(
                           start,
                           route.Points[waypointIndex]) < 18f)
                {
                    waypointIndex++;
                }
                selected.waypointIndex = waypointIndex;
                for (int index = waypointIndex;
                     index < route.Points.Length;
                     index++)
                {
                    output.Add(route.Points[index]);
                }
                return output.Count > 0;
            }
            bool forward = Vector3.Distance(start, route.Points[0]) <=
                           Vector3.Distance(
                               start,
                               route.Points[route.Points.Length - 1]);
            if (forward)
            {
                for (int index = 0; index < route.Points.Length; index++)
                    output.Add(route.Points[index]);
            }
            else
            {
                for (int index = route.Points.Length - 1; index >= 0; index--)
                    output.Add(route.Points[index]);
            }
            return true;
        }

        public bool CanGrantAttack(HordeEnemyVehicle enemy)
        {
            int effectiveEngagements = Mathf.Max(
                engagementCount,
                attackTokensUsed + environmentalCommitments.Count);
            if (!running || enemy == null || settings == null ||
                currentSample.actualPressure >= settings.hardPressureLimit ||
                attackTokensUsed >= AttackTokenCap ||
                effectiveEngagements >= EngagementCap)
            {
                return false;
            }
            // A player deliberately entering a generated trap approach must
            // have one EDPCG engagement seat available for the committed
            // pursuer. Ordinary fire permissions cannot consume that final
            // seat while the trap opportunity is open.
            if (environmentalCommitments.Count == 0 &&
                tacticalMap != null && playerBody != null &&
                tacticalMap.HasEnvironmentalTrapOpportunity(
                    playerBody.worldCenterOfMass) &&
                effectiveEngagements >= Mathf.Max(0, EngagementCap - 1))
            {
                return false;
            }
            if (enemy.IsSuicide &&
                suicideCommitCount >= settings.suicideCommitCap)
            {
                return false;
            }
            if (!enemy.IsSuicide &&
                rangedFireLaneCount >= settings.rangedFireLaneCap)
            {
                return false;
            }
            return true;
        }

        public bool ShouldSpawn
        {
            get
            {
                return running && !IsRosterResolved &&
                       currentSample.actualPressure <
                       settings.hardPressureLimit &&
                       activeCount < PopulationCap;
            }
        }

        public void RecordPathFailure(
            HordeEnemyVehicle enemy,
            EdpcgPathIntent intent,
            string reason)
        {
            if (recorder == null)
                return;
            recorder.RecordEvent(
                elapsed,
                EdpcgTelemetryEventKind.PathFailed,
                string.IsNullOrEmpty(reason) ? intent.ToString() : reason,
                enemy != null ? enemy.RosterMemberId : string.Empty,
                enemy != null ? enemy.BodyPosition : Vector3.zero);
        }

        public bool TryApplyTuning(
            string fieldName,
            string serializedValue,
            EdpcgLiveApplyPolicy policy,
            out string changeId,
            out string error)
        {
            changeId = string.Empty;
            error = string.Empty;
            if (settings == null || string.IsNullOrWhiteSpace(fieldName))
            {
                error = "No active EDPCG settings.";
                return false;
            }
            FieldInfo field = typeof(EdpcgTierSettings).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public);
            if (field == null || field.IsInitOnly)
            {
                error = "Unknown or read-only field: " + fieldName;
                return false;
            }
            if (IsRestartOnlyField(fieldName))
                policy = EdpcgLiveApplyPolicy.RequiresRestart;
            if (!TryParseValue(field.FieldType, serializedValue, out object value))
            {
                error = "Invalid value for " + fieldName + ".";
                return false;
            }
            object previous = field.GetValue(settings);
            if (!originalValues.ContainsKey(fieldName))
                originalValues.Add(fieldName, previous);
            var change = new EdpcgRuntimeChange
            {
                changeId = "change-" + (++changeSequence).ToString("D5"),
                fieldPath = fieldName,
                previousSerializedValue = SerializeValue(previous),
                candidateSerializedValue = SerializeValue(value),
                submittedAt = elapsed,
                applyPolicy = policy,
                state = policy == EdpcgLiveApplyPolicy.ApplyNow
                    ? EdpcgRuntimeChangeState.Applied
                    : EdpcgRuntimeChangeState.Pending,
                appliedAt = policy == EdpcgLiveApplyPolicy.ApplyNow
                    ? elapsed
                    : -1f,
                safeBoundaryReason = policy ==
                                     EdpcgLiveApplyPolicy.ApplyAtSafeBoundary
                    ? "next-phase-change"
                    : string.Empty
            };
            if (policy == EdpcgLiveApplyPolicy.ApplyNow)
            {
                field.SetValue(settings, value);
                settings.ValidateInPlace();
            }
            recorder.RecordChange(change);
            recorder.RecordEvent(
                elapsed,
                EdpcgTelemetryEventKind.TuningChange,
                change.changeId + ":" + fieldName);
            changeId = change.changeId;
            return true;
        }

        public bool RevertChange(string changeId)
        {
            EdpcgRuntimeChange change = recorder?.FindChange(changeId);
            if (change == null ||
                change.state == EdpcgRuntimeChangeState.Reverted)
            {
                return false;
            }
            FieldInfo field = typeof(EdpcgTierSettings).GetField(
                change.fieldPath,
                BindingFlags.Instance | BindingFlags.Public);
            if (field == null ||
                !TryParseValue(
                    field.FieldType,
                    change.previousSerializedValue,
                    out object previous))
            {
                return false;
            }
            field.SetValue(settings, previous);
            settings.ValidateInPlace();
            change.state = EdpcgRuntimeChangeState.Reverted;
            return true;
        }

        public void RevertAllTuning()
        {
            if (baselineSettings == null)
                return;
            settings = baselineSettings.ValidatedCopy();
            originalValues.Clear();
            IReadOnlyList<EdpcgRuntimeChange> changes = recorder.Changes;
            for (int index = 0; index < changes.Count; index++)
            {
                if (changes[index].state == EdpcgRuntimeChangeState.Applied ||
                    changes[index].state == EdpcgRuntimeChangeState.Pending)
                {
                    changes[index].state = EdpcgRuntimeChangeState.Reverted;
                }
            }
        }

        void BuildRoster()
        {
            roster.Clear();
            memberById.Clear();
            CreditedKills = 0;
            ResolvedCount = 0;
            var roles = new List<HordeEnemyRole>(settings.rosterCount);
            AppendRoles(roles, HordeEnemyRole.Interceptor,
                settings.interceptorCount);
            AppendRoles(roles, HordeEnemyRole.Striker, settings.strikerCount);
            AppendRoles(roles, HordeEnemyRole.Gunship, settings.gunshipCount);
            var random = new System.Random(rosterSeed);
            for (int index = roles.Count - 1; index > 0; index--)
            {
                int swap = random.Next(index + 1);
                HordeEnemyRole value = roles[index];
                roles[index] = roles[swap];
                roles[swap] = value;
            }
            int environmentalPursuersRemaining =
                settings.environmentalPursuerCount;
            for (int index = 0; index < roles.Count; index++)
            {
                bool environmentalPursuer =
                    roles[index] == HordeEnemyRole.Interceptor &&
                    environmentalPursuersRemaining > 0;
                if (environmentalPursuer)
                    environmentalPursuersRemaining--;
                var member = new EdpcgRosterMember
                {
                    rosterMemberId = string.Format(
                        CultureInfo.InvariantCulture,
                        "edpcg-{0:X8}-{1:D3}",
                        rosterSeed,
                        index),
                    role = roles[index],
                    environmentalPursuer = environmentalPursuer,
                    state = EdpcgRosterState.Unspawned,
                    resolutionReason = EdpcgEnemyResolutionReason.None,
                    rosterIndex = index,
                    healthRatio = 1f
                };
                roster.Add(member);
                memberById.Add(member.rosterMemberId, member);
            }
        }

        void UpdatePressure(float deltaTime)
        {
            float populationDenominator = Mathf.Max(1f, settings.populationCap);
            float threatDenominator = Mathf.Max(
                1f,
                settings.populationCap * 1.5f);
            float enemyThreat = Mathf.Clamp01(
                activeThreatCost / threatDenominator * 0.58f +
                engagementCount / Mathf.Max(1f, settings.engagementCap) * 0.24f +
                attackTokensUsed / Mathf.Max(1f, settings.attackTokenCap) * 0.18f);
            float suicideNavigation = Mathf.Clamp01(
                suicideCommitCount /
                Mathf.Max(1f, settings.suicideCommitCap));
            float rangedNavigation = Mathf.Clamp01(
                rangedFireLaneCount /
                Mathf.Max(1f, settings.rangedFireLaneCap));
            float environmentalPursuit = Mathf.Clamp01(
                environmentalCommitments.Count /
                Mathf.Max(1f, settings.engagementCap));
            float navigation = Mathf.Clamp01(
                navigationRecoveryCount / populationDenominator * 0.45f +
                suicideNavigation * 0.30f +
                rangedNavigation * 0.25f +
                environmentalPursuit * 0.35f);
            float healthStrain = playerGraph != null
                ? 1f - Mathf.Clamp01(playerGraph.OverallHealthRatio)
                : 0f;
            float cpuStrain = playerGraph != null
                ? 1f - Mathf.Clamp01(playerGraph.ConnectedCpuRatio)
                : 0f;
            float motionStrain = playerBody != null
                ? Mathf.InverseLerp(1.5f, 5.5f, playerBody.angularVelocity.magnitude)
                : 0f;
            float playerStrain = Mathf.Clamp01(
                healthStrain * 0.55f + cpuStrain * 0.30f +
                motionStrain * 0.15f);
            Vector3 playerPosition = playerBody != null
                ? playerBody.worldCenterOfMass
                : Vector3.zero;
            float playerSpeed = playerBody != null
                ? playerBody.velocity.magnitude
                : 0f;
            float environment = tacticalMap != null
                ? tacticalMap.EstimateMeasuredEnvironmentPressure(
                    playerPosition,
                    playerSpeed,
                    motionStrain,
                    out _)
                : 0f;
            float raw = Mathf.Clamp01(
                enemyThreat * settings.enemyThreatWeight +
                navigation * settings.navigationWeight +
                environment * settings.environmentWeight +
                playerStrain * settings.playerStrainWeight);
            float smoothing = 1f - Mathf.Exp(
                -Mathf.Max(0f, deltaTime) /
                Mathf.Max(0.05f, settings.pressureSmoothingSeconds));
            smoothedPressure = Mathf.Lerp(smoothedPressure, raw, smoothing);
            if (deltaTime > PressureEpsilon)
            {
                float derivative = (raw - previousRawPressure) / deltaTime;
                pressureVelocity = Mathf.Lerp(
                    pressureVelocity,
                    derivative,
                    1f - Mathf.Exp(-deltaTime / 1.5f));
            }
            previousRawPressure = raw;
            settings.ResolveTargetBand(
                phase,
                out float targetMinimum,
                out float targetMaximum);
            CountRosterStates(
                out int unspawned,
                out int queued,
                out int spawnRecovery,
                out int rosterActive,
                out int rosterNavigationRecovery);
            currentSample = new EdpcgPressureSample
            {
                missionTime = elapsed,
                phase = phase,
                targetPressureMinimum = targetMinimum,
                targetPressureMaximum = targetMaximum,
                actualPressure = smoothedPressure,
                forecastPressure4Seconds = Mathf.Clamp01(
                    smoothedPressure + pressureVelocity * 4f),
                forecastPressure8Seconds = Mathf.Clamp01(
                    smoothedPressure + pressureVelocity * 8f),
                enemyThreatPressure = enemyThreat,
                measuredEnvironmentPressure = environment,
                navigationPressure = navigation,
                suicideNavigationPressure = suicideNavigation,
                rangedNavigationPressure = rangedNavigation,
                committedNavigationPressure = Mathf.Clamp01(
                    Mathf.Max(
                        attackTokensUsed /
                        Mathf.Max(1f, settings.attackTokenCap),
                        environmentalPursuit)),
                environmentalPursuitPressure = environmentalPursuit,
                playerStrain = playerStrain,
                playerDamageAssist = Mathf.Clamp01(
                    Mathf.InverseLerp(0.55f, 0.85f, smoothedPressure) *
                    playerStrain),
                rosterCount = roster.Count,
                unspawnedCount = unspawned,
                queuedCount = queued,
                spawnRecoveryCount = spawnRecovery,
                activeCount = Mathf.Max(activeCount, rosterActive),
                engagementCount = engagementCount,
                attackTokensUsed = attackTokensUsed,
                suicideCommitCount = suicideCommitCount,
                rangedFireLaneCount = rangedFireLaneCount,
                navigationRecoveryCount = Mathf.Max(
                    navigationRecoveryCount,
                    rosterNavigationRecovery),
                environmentalPursuitCount =
                    environmentalCommitments.Count,
                resolvedCount = ResolvedCount,
                activeTacticalAreaId = activeArea != null
                    ? activeArea.StableId
                    : string.Empty,
                activeTacticalAreaKind = activeArea != null
                    ? activeArea.Kind
                    : EdpcgTacticalAreaKind.None
            };
        }

        void UpdateAreaState()
        {
            if (playerBody == null || tacticalMap == null)
                return;
            tacticalMap.TryGetArea(
                playerBody.worldCenterOfMass,
                out EdpcgRuntimeArea candidate);
            if (candidate != areaCandidate)
            {
                areaCandidate = candidate;
                areaCandidateSince = elapsed;
            }
            if (candidate != null && candidate != activeArea &&
                elapsed - areaCandidateSince >= settings.areaEnterDwellSeconds)
            {
                if (activeArea != null)
                {
                    recorder.RecordEvent(
                        elapsed,
                        EdpcgTelemetryEventKind.AreaExited,
                        activeArea.Kind.ToString(),
                        areaId: activeArea.StableId);
                }
                activeArea = candidate;
                areaExitedSince = -1f;
                recorder.RecordEvent(
                    elapsed,
                    EdpcgTelemetryEventKind.AreaEntered,
                    activeArea.Kind.ToString(),
                    areaId: activeArea.StableId);
            }
            else if (candidate == null && activeArea != null)
            {
                if (areaExitedSince < 0f)
                    areaExitedSince = elapsed;
                if (elapsed - areaExitedSince >= settings.areaExitDwellSeconds)
                {
                    recorder.RecordEvent(
                        elapsed,
                        EdpcgTelemetryEventKind.AreaExited,
                        activeArea.Kind.ToString(),
                        areaId: activeArea.StableId);
                    activeArea = null;
                    areaExitedSince = -1f;
                }
            }
        }

        void ApplyPendingChangesAtSafeBoundary(string reason)
        {
            IReadOnlyList<EdpcgRuntimeChange> changes = recorder.Changes;
            for (int index = 0; index < changes.Count; index++)
            {
                EdpcgRuntimeChange change = changes[index];
                if (change.state != EdpcgRuntimeChangeState.Pending ||
                    change.applyPolicy !=
                    EdpcgLiveApplyPolicy.ApplyAtSafeBoundary)
                {
                    continue;
                }
                FieldInfo field = typeof(EdpcgTierSettings).GetField(
                    change.fieldPath,
                    BindingFlags.Instance | BindingFlags.Public);
                if (field == null ||
                    !TryParseValue(
                        field.FieldType,
                        change.candidateSerializedValue,
                        out object candidate))
                {
                    change.state = EdpcgRuntimeChangeState.Cancelled;
                    continue;
                }
                field.SetValue(settings, candidate);
                settings.ValidateInPlace();
                change.state = EdpcgRuntimeChangeState.Applied;
                change.appliedAt = elapsed;
                change.safeBoundaryReason = reason;
            }
        }

        void CountRosterStates(
            out int unspawned,
            out int queued,
            out int spawnRecovery,
            out int rosterActive,
            out int rosterNavigationRecovery)
        {
            unspawned = 0;
            queued = 0;
            spawnRecovery = 0;
            rosterActive = 0;
            rosterNavigationRecovery = 0;
            for (int index = 0; index < roster.Count; index++)
            {
                switch (roster[index].state)
                {
                    case EdpcgRosterState.Unspawned:
                        unspawned++;
                        break;
                    case EdpcgRosterState.Queued:
                        queued++;
                        break;
                    case EdpcgRosterState.SpawnRecovery:
                        spawnRecovery++;
                        break;
                    case EdpcgRosterState.Active:
                        rosterActive++;
                        break;
                    case EdpcgRosterState.NavigationRecovery:
                        rosterNavigationRecovery++;
                        break;
                }
            }
        }

        EdpcgRosterMember FindUnspawned(HordeEnemyRole role)
        {
            for (int index = 0; index < roster.Count; index++)
            {
                EdpcgRosterMember member = roster[index];
                if (member.state == EdpcgRosterState.Unspawned &&
                    member.role == role)
                {
                    return member;
                }
            }
            return null;
        }

        EdpcgRosterMember FindUnspawnedEnvironmentalPursuer()
        {
            for (int index = 0; index < roster.Count; index++)
            {
                EdpcgRosterMember member = roster[index];
                if (member.state == EdpcgRosterState.Unspawned &&
                    member.environmentalPursuer)
                {
                    return member;
                }
            }
            return null;
        }

        EdpcgRosterMember FindUnspawnedAnyRole()
        {
            for (int index = 0; index < roster.Count; index++)
            {
                if (roster[index].state == EdpcgRosterState.Unspawned)
                    return roster[index];
            }
            return null;
        }

        bool TryGetMember(
            string rosterMemberId,
            out EdpcgRosterMember member)
        {
            if (string.IsNullOrEmpty(rosterMemberId))
            {
                member = null;
                return false;
            }
            return memberById.TryGetValue(rosterMemberId, out member);
        }

        static void AppendRoles(
            List<HordeEnemyRole> output,
            HordeEnemyRole role,
            int count)
        {
            for (int index = 0; index < Mathf.Max(0, count); index++)
                output.Add(role);
        }

        static bool IsRestartOnlyField(string fieldName)
        {
            return string.Equals(fieldName, "rosterCount", StringComparison.Ordinal) ||
                   string.Equals(fieldName, "interceptorCount", StringComparison.Ordinal) ||
                   string.Equals(fieldName, "strikerCount", StringComparison.Ordinal) ||
                   string.Equals(fieldName, "gunshipCount", StringComparison.Ordinal) ||
                   string.Equals(fieldName, "environmentalPursuerCount",
                       StringComparison.Ordinal) ||
                   string.Equals(fieldName, "planetTier", StringComparison.Ordinal);
        }

        static bool TryParseValue(
            Type type,
            string serialized,
            out object value)
        {
            if (type == typeof(int) && int.TryParse(
                    serialized,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int integer))
            {
                value = integer;
                return true;
            }
            if (type == typeof(float) && float.TryParse(
                    serialized,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float number))
            {
                value = number;
                return true;
            }
            if (type == typeof(bool) && bool.TryParse(serialized, out bool flag))
            {
                value = flag;
                return true;
            }
            if (type == typeof(string))
            {
                value = serialized ?? string.Empty;
                return true;
            }
            value = null;
            return false;
        }

        static string SerializeValue(object value)
        {
            return value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value?.ToString() ?? string.Empty;
        }

        void OnDisable()
        {
            if (running)
                EndSession();
        }
    }
}
