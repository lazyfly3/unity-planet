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

        sealed class PendingGridRouteTarget
        {
            public string stableId = string.Empty;
            public string routeId = string.Empty;
            public int segmentIndex = -1;
            public float segmentT;
            public Vector3 routeEntryWorldPosition;
            public Vector3 destinationWorldPosition;
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
        readonly Dictionary<string, PendingGridRouteTarget>
            pendingGridRouteTargets =
                new Dictionary<string, PendingGridRouteTarget>(
                    StringComparer.Ordinal);

        HordeCombatDirector director;
        Rigidbody playerBody;
        VehicleStructureGraph playerGraph;
        EdpcgDifficultyProfile profile;
        EdpcgTierSettings settings;
        EdpcgTierSettings baselineSettings;
        EdpcgCityTacticalChallengeProfile cityChallengeProfile;
        EdpcgCityTacticalChallengeSettings cityChallengeSettings;
        FinitePlanetUrbanCombatRuntime urbanRuntime;
        EdpcgCityTacticalRuntimeMap tacticalMap;
        readonly EdpcgGridFireAnalysis[] gridFireAnalyses =
            new EdpcgGridFireAnalysis[3];
        EdpcgGridFireAnalysis activeGridFireAnalysis;
        EdpcgGridFireCell activeGridFireCell;
        EdpcgCityTacticalChallengeReport cityChallengeReport;
        string gridCellCandidateId = string.Empty;
        float gridCellCandidateSince;
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
        int pursuingThreatCount;
        int closeApproachThreatCount;
        int navigationRecoveryCount;
        float activeThreatCost;
        int pressureDirectionCount;
        int pressureDirectionMask;
        int changeSequence;
        int rosterSeed;
        int citySeed;
        int pressureAssistLevel;
        int pressureBrakeLevel;
        float lowPressureControlSeconds;
        float highPressureControlSeconds;
        float pressureControlReleaseSeconds;
        int facilityAssaultSpawnUrgency;
        float nextFacilityAssaultObjectiveAlertAt;
        readonly HashSet<int> destroyedFacilityAssaultObjectives =
            new HashSet<int>();
        string missionId = string.Empty;
        bool configured;
        bool running;

        public EdpcgDifficultyProfile Profile => profile;
        public EdpcgTierSettings Settings => settings;
        public EdpcgTierSettings BaselineSettings => baselineSettings;
        public EdpcgCityTacticalChallengeProfile CityChallengeProfile =>
            cityChallengeProfile;
        public EdpcgCityTacticalChallengeSettings CityChallengeSettings =>
            cityChallengeSettings;
        public EdpcgGridFireAnalysis ActiveGridFireAnalysis =>
            activeGridFireAnalysis;
        public EdpcgGridFireCell ActiveGridFireCell => activeGridFireCell;
        public EdpcgCityTacticalChallengeReport CityChallengeReport =>
            cityChallengeReport;
        public float ActiveGridCellDwellSeconds => activeGridFireCell != null
            ? Mathf.Max(0f, elapsed - gridCellCandidateSince)
            : 0f;
        public EdpcgCityTacticalRuntimeMap TacticalMap => tacticalMap;
        public EdpcgRouteReservationService Reservations => reservations;
        public FinitePlanetUrbanCombatRuntime UrbanRuntime => urbanRuntime;
        public Vector3 PlayerWorldCenter => playerBody != null
            ? playerBody.worldCenterOfMass
            : Vector3.zero;
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
        /// <summary>
        /// A bounded, one-wave scheduling hint raised only by the formal
        /// industrial-outpost objective. The horde director may use this to
        /// bring its next scheduling check forward; all normal population,
        /// hard-pressure, fixed-roster and spawn-position gates still apply.
        /// </summary>
        public int SpawnUrgency =>
            EdpcgFacilityAssaultPacingPolicy.Applies(missionId)
                ? facilityAssaultSpawnUrgency
                : 0;
        public int FacilityAssaultDestroyedObjectiveCount =>
            destroyedFacilityAssaultObjectives.Count;
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
                int cap = BasePopulationCapForPressure() +
                          pressureAssistLevel - pressureBrakeLevel * 2;
                return Mathf.Clamp(cap, 1, settings.populationCap);
            }
        }

        public int EngagementCap
        {
            get
            {
                if (settings == null)
                    return 1;
                int cap = BaseEngagementCapForPressure();
                cap += Mathf.CeilToInt(pressureAssistLevel * 0.5f) -
                       pressureBrakeLevel;
                return Mathf.Clamp(
                    cap,
                    1,
                    Mathf.Min(settings.engagementCap, PopulationCap));
            }
        }

        public int AttackTokenCap
        {
            get
            {
                if (settings == null)
                    return 1;
                int cap = BaseAttackTokenCapForPressure();
                cap += pressureAssistLevel / 2 - pressureBrakeLevel;
                return Mathf.Clamp(cap, 1, settings.attackTokenCap);
            }
        }

        int BasePopulationCapForPressure()
        {
            if (settings == null)
                return 1;
            float multiplier = phase == EdpcgEncounterPhase.Preview
                ? 0.45f
                : phase == EdpcgEncounterPhase.Engage
                    ? 0.82f
                    : phase == EdpcgEncounterPhase.Peak ? 1f : 0.58f;
            return Mathf.Clamp(
                Mathf.CeilToInt(settings.populationCap * multiplier),
                1,
                settings.populationCap);
        }

        int BaseEngagementCapForPressure()
        {
            if (settings == null)
                return 1;
            int cap = Mathf.Min(
                settings.engagementCap,
                BasePopulationCapForPressure());
            if (phase == EdpcgEncounterPhase.Preview ||
                phase == EdpcgEncounterPhase.Recover)
            {
                cap = Mathf.CeilToInt(cap * 0.55f);
            }
            return Mathf.Clamp(
                cap,
                1,
                Mathf.Min(settings.engagementCap, settings.populationCap));
        }

        int BaseAttackTokenCapForPressure()
        {
            if (settings == null)
                return 1;
            return phase == EdpcgEncounterPhase.Preview ||
                   phase == EdpcgEncounterPhase.Recover
                ? 1
                : settings.attackTokenCap;
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
                if (pressureAssistLevel > 0)
                    level++;
                level -= pressureBrakeLevel;
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
                multiplier *= Mathf.Pow(0.78f, pressureAssistLevel);
                multiplier *= Mathf.Pow(1.25f, pressureBrakeLevel);
                multiplier *=
                    EdpcgFacilityAssaultPacingPolicy.SpawnIntervalMultiplier(
                        missionId);
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
            urbanRuntime = urban;
            cityChallengeProfile =
                EdpcgCityTacticalChallengeProfile.LoadOrCreateMemoryDefault();
            profile = difficultyProfile ??
                      cityChallengeProfile.ordinaryEnemyProfile ??
                      EdpcgDifficultyProfile.LoadOrCreateMemoryDefault();
            settings = profile.Resolve(zeroBasedPlanetTier);
            missionId = targetMissionId ?? string.Empty;
            // Resolve() returns a session copy. Applying the facility policy to
            // that copy cannot mutate the shared difficulty asset or its
            // integrationMode, and all other missions remain byte-for-byte on
            // their authored settings.
            EdpcgFacilityAssaultPacingPolicy.ApplySessionSettings(
                missionId,
                settings);
            baselineSettings = settings.ValidatedCopy();
            cityChallengeSettings = cityChallengeProfile.Resolve(
                zeroBasedPlanetTier);
            citySeed = deterministicSeed;
            rosterSeed = deterministicSeed ^ unchecked((int)0x45D9F3B);
            tacticalMap = EdpcgCityTacticalRuntimeMap.Build(urban);
            float actualShipWidth = ResolvePlayerShipWidth(targetPlayerBody);
            EdpcgAirThreatAnalysisOptions threatOptions =
                EdpcgAirThreatAnalysisOptions.CreateDefault(
                    urban != null ? urban.CitySettings : null,
                    zeroBasedPlanetTier);
            threatOptions.tierSettings = settings;
            threatOptions.challengeSettings = cityChallengeSettings;
            bool buildRuntimeThreatDomain = urban != null &&
                cityChallengeSettings != null &&
                cityChallengeSettings.enableOrdinaryRangedReposition;
            for (int layer = 0; layer < gridFireAnalyses.Length; layer++)
            {
                gridFireAnalyses[layer] = buildRuntimeThreatDomain
                    ? EdpcgGridFireAnalyzer.Build(
                        urban,
                        (EdpcgFireAnalysisAltitudeLayer)layer,
                        actualShipWidth,
                        threatOptions)
                    : null;
            }
            if (buildRuntimeThreatDomain)
            {
                EdpcgGridFireAnalyzer.LinkAltitudeLayers(
                    gridFireAnalyses[0],
                    gridFireAnalyses[1],
                    gridFireAnalyses[2],
                    urban.RuntimeGeometrySnapshot,
                    threatOptions);
            }
            cityChallengeReport =
                EdpcgCityTacticalChallengeEvaluator.Evaluate(
                    cityChallengeSettings,
                    gridFireAnalyses[0],
                    gridFireAnalyses[1],
                    gridFireAnalyses[2],
                    tacticalMap);
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
            pendingGridRouteTargets.Clear();
            ClearEnvironmentalTrapCommitments();
            originalValues.Clear();
            elapsed = 0f;
            nextTelemetryAt = 0f;
            smoothedPressure = 0f;
            previousRawPressure = 0f;
            pressureVelocity = 0f;
            pressureAssistLevel = 0;
            pressureBrakeLevel = 0;
            lowPressureControlSeconds = 0f;
            highPressureControlSeconds = 0f;
            pressureControlReleaseSeconds = 0f;
            facilityAssaultSpawnUrgency = 0;
            nextFacilityAssaultObjectiveAlertAt = 0f;
            destroyedFacilityAssaultObjectives.Clear();
            activeArea = null;
            areaCandidate = null;
            areaCandidateSince = 0f;
            areaExitedSince = -1f;
            activeGridFireAnalysis = null;
            activeGridFireCell = null;
            gridCellCandidateId = string.Empty;
            gridCellCandidateSince = 0f;
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
            pendingGridRouteTargets.Clear();
            facilityAssaultSpawnUrgency = 0;
            destroyedFacilityAssaultObjectives.Clear();
            EdpcgRuntimeRegistry.Unregister(this);
        }

        /// <summary>
        /// Reports real damage to an industrial-outpost objective. This does
        /// not add pressure and cannot create an enemy. It only releases one
        /// bounded pressure-assist step and raises a scheduling hint, with a
        /// deterministic cooldown so automatic weapons cannot request a wave
        /// every frame.
        /// </summary>
        public bool NotifyFacilityAssaultObjectiveDamaged()
        {
            if (!running || settings == null ||
                !EdpcgFacilityAssaultPacingPolicy.Applies(missionId) ||
                elapsed < nextFacilityAssaultObjectiveAlertAt)
            {
                return false;
            }

            nextFacilityAssaultObjectiveAlertAt = elapsed +
                EdpcgFacilityAssaultPacingPolicy.ObjectiveAlertCooldownSeconds;
            facilityAssaultSpawnUrgency =
                EdpcgFacilityAssaultPacingPolicy.MergeSpawnUrgency(
                    facilityAssaultSpawnUrgency,
                    1);
            pressureAssistLevel =
                EdpcgFacilityAssaultPacingPolicy.ResolveAssistFloor(
                    pressureAssistLevel,
                    1,
                    settings.maximumPressureAssistSteps);
            recorder?.RecordEvent(
                elapsed,
                EdpcgTelemetryEventKind.StateChanged,
                "facility-objective-damaged");
            return true;
        }

        /// <summary>
        /// Reports destruction of one industrial-outpost objective. Supplying
        /// its stable objective index makes duplicate callbacks idempotent.
        /// The no-argument form remains available for simple callers.
        /// </summary>
        public bool NotifyFacilityAssaultObjectiveDestroyed(
            int objectiveIndex = -1)
        {
            if (!running || settings == null ||
                !EdpcgFacilityAssaultPacingPolicy.Applies(missionId))
            {
                return false;
            }
            if (objectiveIndex >= 0 &&
                !destroyedFacilityAssaultObjectives.Add(objectiveIndex))
            {
                return false;
            }
            if (objectiveIndex < 0)
            {
                int anonymousIndex = 0;
                while (destroyedFacilityAssaultObjectives.Contains(
                           anonymousIndex))
                {
                    anonymousIndex++;
                }
                destroyedFacilityAssaultObjectives.Add(anonymousIndex);
            }

            nextFacilityAssaultObjectiveAlertAt = Mathf.Max(
                nextFacilityAssaultObjectiveAlertAt,
                elapsed +
                EdpcgFacilityAssaultPacingPolicy.ObjectiveAlertCooldownSeconds);
            facilityAssaultSpawnUrgency =
                EdpcgFacilityAssaultPacingPolicy.MergeSpawnUrgency(
                    facilityAssaultSpawnUrgency,
                    2);
            pressureAssistLevel =
                EdpcgFacilityAssaultPacingPolicy.ResolveAssistFloor(
                    pressureAssistLevel,
                    2,
                    settings.maximumPressureAssistSteps);
            recorder?.RecordEvent(
                elapsed,
                EdpcgTelemetryEventKind.StateChanged,
                "facility-objective-destroyed:" +
                objectiveIndex.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        /// <summary>
        /// Atomically consumes the current facility scheduling hint. Consumers
        /// should only advance their next scheduling check; ShouldSpawn and the
        /// existing director gates remain the authority for actual spawning.
        /// </summary>
        public bool TryConsumeSpawnUrgency(out int urgency)
        {
            urgency = 0;
            if (!running ||
                !EdpcgFacilityAssaultPacingPolicy.Applies(missionId) ||
                facilityAssaultSpawnUrgency <= 0)
            {
                return false;
            }

            urgency = facilityAssaultSpawnUrgency;
            facilityAssaultSpawnUrgency = 0;
            recorder?.RecordEvent(
                elapsed,
                EdpcgTelemetryEventKind.StateChanged,
                "facility-spawn-urgency-consumed:" +
                urgency.ToString(CultureInfo.InvariantCulture));
            return true;
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
                lowPressureControlSeconds = 0f;
                highPressureControlSeconds = 0f;
                pressureControlReleaseSeconds = 0f;
                if (phase == EdpcgEncounterPhase.Preview ||
                    phase == EdpcgEncounterPhase.Recover)
                {
                    pressureAssistLevel = Mathf.Min(
                        pressureAssistLevel,
                        1);
                }
                recorder.RecordEvent(
                    elapsed,
                    EdpcgTelemetryEventKind.PhaseChanged,
                    phase.ToString());
                ApplyPendingChangesAtSafeBoundary("phase-change");
            }
            reservations.ReleaseExpired(Time.time);
            ReconcileEnvironmentalTrapState();
            UpdateAreaState();
            UpdateGridFireState();
            UpdatePressureControl(safeDelta);
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
            float currentActiveThreatCost)
        {
            UpdateDirectorSnapshot(
                currentActiveCount,
                currentEngagementCount,
                currentAttackTokens,
                currentSuicideCommits,
                currentRangedFireLanes,
                currentNavigationRecoveries,
                currentActiveThreatCost,
                0,
                0,
                0,
                0);
        }

        public void UpdateDirectorSnapshot(
            int currentActiveCount,
            int currentEngagementCount,
            int currentAttackTokens,
            int currentSuicideCommits,
            int currentRangedFireLanes,
            int currentNavigationRecoveries,
            float currentActiveThreatCost,
            int currentPressureDirectionCount)
        {
            UpdateDirectorSnapshot(
                currentActiveCount,
                currentEngagementCount,
                currentAttackTokens,
                currentSuicideCommits,
                currentRangedFireLanes,
                currentNavigationRecoveries,
                currentActiveThreatCost,
                currentPressureDirectionCount,
                0,
                0,
                0);
        }

        public void UpdateDirectorSnapshot(
            int currentActiveCount,
            int currentEngagementCount,
            int currentAttackTokens,
            int currentSuicideCommits,
            int currentRangedFireLanes,
            int currentNavigationRecoveries,
            float currentActiveThreatCost,
            int currentPressureDirectionCount,
            int currentPressureDirectionMask)
        {
            UpdateDirectorSnapshot(
                currentActiveCount,
                currentEngagementCount,
                currentAttackTokens,
                currentSuicideCommits,
                currentRangedFireLanes,
                currentNavigationRecoveries,
                currentActiveThreatCost,
                currentPressureDirectionCount,
                currentPressureDirectionMask,
                0,
                0);
        }

        public void UpdateDirectorSnapshot(
            int currentActiveCount,
            int currentEngagementCount,
            int currentAttackTokens,
            int currentSuicideCommits,
            int currentRangedFireLanes,
            int currentNavigationRecoveries,
            float currentActiveThreatCost,
            int currentPressureDirectionCount,
            int currentPressureDirectionMask,
            int currentPursuingThreatCount,
            int currentCloseApproachThreatCount)
        {
            activeCount = Mathf.Max(0, currentActiveCount);
            engagementCount = Mathf.Max(0, currentEngagementCount);
            attackTokensUsed = Mathf.Max(0, currentAttackTokens);
            suicideCommitCount = Mathf.Max(0, currentSuicideCommits);
            rangedFireLaneCount = Mathf.Max(0, currentRangedFireLanes);
            pursuingThreatCount = Mathf.Max(0, currentPursuingThreatCount);
            closeApproachThreatCount = Mathf.Max(
                0,
                currentCloseApproachThreatCount);
            navigationRecoveryCount = Mathf.Max(
                0,
                currentNavigationRecoveries);
            activeThreatCost = Mathf.Max(0, currentActiveThreatCost);
            pressureDirectionCount = Mathf.Clamp(
                currentPressureDirectionCount,
                0,
                8);
            pressureDirectionMask = currentPressureDirectionMask & 0xFF;
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
            bool resolvedByGrid = TryResolveGridPressureDestination(
                    enemy,
                    intent,
                    out destination,
                    out areaId,
                    out routeId);
            PendingGridRouteTarget gridTarget = null;
            if (resolvedByGrid)
            {
                pendingGridRouteTargets.TryGetValue(
                    enemy.RosterMemberId, out gridTarget);
            }
            else
            {
                pendingGridRouteTargets.Remove(enemy.RosterMemberId);
            }
            if (!resolvedByGrid && !tacticalMap.TrySelectRoleDestination(
                    enemy.Role,
                    enemy.RosterIndex,
                    playerBody.worldCenterOfMass,
                    intent,
                    settings.integrationMode >=
                    EdpcgIntegrationMode.TacticalAssignments,
                    out destination,
                    out areaId,
                    out routeId))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(routeId) &&
                tacticalMap.TryGetRoute(routeId, out EdpcgRuntimeRoute route))
            {
                if (reservations.TryGetOwnerReservation(
                        enemy.RosterMemberId,
                        out EdpcgRouteReservation existing) &&
                    string.Equals(
                        existing.routeId,
                        routeId,
                        StringComparison.Ordinal) &&
                    ReservationMatchesGridTarget(existing, gridTarget))
                {
                    float remaining = gridTarget != null
                        ? EstimateTargetedRouteSeconds(
                            route, enemy.BodyPosition, gridTarget,
                            enemy.Profile != null
                                ? enemy.Profile.maximumSpeed
                                : 48f,
                            settings.semanticReplanSeconds)
                        : route.EstimatedTravelSeconds;
                    reservations.Renew(
                        existing.reservationId,
                        Time.time,
                        settings.reservationLeaseSeconds,
                        remaining);
                    reservationId = existing.reservationId;
                }
                else
                {
                    reservations.ReleaseOwner(enemy.RosterMemberId);
                }
                int direction = 1;
                if (gridTarget != null && route.AllowReverse &&
                    route.Points != null && route.Points.Length >= 2)
                {
                    ProjectRouteProgress(
                        route.Points,
                        enemy.BodyPosition,
                        out _, out _, out float currentProgress);
                    float targetProgress = RouteProgressDistance(
                        route.Points,
                        gridTarget.segmentIndex,
                        gridTarget.segmentT);
                    direction = currentProgress <= targetProgress ? 1 : -1;
                }
                else if (route.AllowReverse && route.Points != null &&
                         route.Points.Length >= 2)
                {
                    float startDistance = Vector3.Distance(
                        enemy.BodyPosition,
                        route.Points[0]);
                    float endDistance = Vector3.Distance(
                        enemy.BodyPosition,
                        route.Points[route.Points.Length - 1]);
                    direction = startDistance <= endDistance ? 1 : -1;
                }
                if (!string.IsNullOrEmpty(reservationId))
                {
                    recorder.RecordEvent(
                        elapsed,
                        EdpcgTelemetryEventKind.PathRequested,
                        intent + ":reuse-reservation",
                        enemy.RosterMemberId,
                        enemy.BodyPosition,
                        0f,
                        areaId,
                        routeId);
                    return true;
                }
                float travelSeconds = gridTarget != null
                    ? EstimateTargetedRouteSeconds(
                        route, enemy.BodyPosition, gridTarget,
                        enemy.Profile != null
                            ? enemy.Profile.maximumSpeed
                            : 48f,
                        settings.semanticReplanSeconds)
                    : route.EstimatedTravelSeconds;
                bool reserved = gridTarget != null
                    ? reservations.TryReserveToProgress(
                        enemy.RosterMemberId,
                        routeId,
                        Time.time,
                        travelSeconds,
                        settings.reservationLeaseSeconds,
                        intent == EdpcgPathIntent.BreakContact ? 2 : 1,
                        direction,
                        gridTarget.segmentIndex,
                        gridTarget.segmentT,
                        gridTarget.routeEntryWorldPosition,
                        gridTarget.stableId,
                        out reservationId)
                    : reservations.TryReserve(
                        enemy.RosterMemberId,
                        routeId,
                        Time.time,
                        travelSeconds,
                        settings.reservationLeaseSeconds,
                        intent == EdpcgPathIntent.BreakContact ? 2 : 1,
                        direction,
                        out reservationId);
                if (reserved)
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
                else if (gridTarget == null && tacticalMap.TryGetRoute(
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
                    if (gridTarget != null)
                    {
                        pendingGridRouteTargets.Remove(
                            enemy.RosterMemberId);
                        return false;
                    }
                }
            }
            pendingGridRouteTargets.Remove(enemy.RosterMemberId);
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

        bool TryResolveGridPressureDestination(
            HordeEnemyVehicle enemy,
            EdpcgPathIntent intent,
            out Vector3 destination,
            out string areaId,
            out string routeId)
        {
            destination = Vector3.zero;
            areaId = string.Empty;
            routeId = string.Empty;
            if (enemy == null || cityChallengeSettings == null ||
                !cityChallengeSettings.enableOrdinaryRangedReposition ||
                settings == null ||
                settings.integrationMode <
                EdpcgIntegrationMode.TacticalAssignments ||
                activeGridFireCell == null || !activeGridFireCell.flyable ||
                elapsed - gridCellCandidateSince <
                cityChallengeSettings.playerCellDwellSeconds)
            {
                return false;
            }
            if (intent != EdpcgPathIntent.Probe &&
                intent != EdpcgPathIntent.MaskedFlank &&
                intent != EdpcgPathIntent.RangedPerch &&
                intent != EdpcgPathIntent.Suppress &&
                intent != EdpcgPathIntent.MaskedHold)
            {
                return false;
            }
            EdpcgGridFireSourceKind desiredKind;
            if (enemy.Role == HordeEnemyRole.Striker)
            {
                if (!cityChallengeSettings.allowStrikerReposition)
                    return false;
                desiredKind = EdpcgGridFireSourceKind.Striker;
            }
            else if (enemy.Role == HordeEnemyRole.Gunship)
            {
                if (!cityChallengeSettings.allowGunshipReposition)
                    return false;
                desiredKind = EdpcgGridFireSourceKind.Gunship;
            }
            else
            {
                // Interceptors remain governed by their suicide approach and
                // environmental-trap commitment. They are never a gunline.
                return false;
            }

            if (activeGridFireCell.safeExitCount <
                    cityChallengeSettings.minimumSafeExitCount ||
                activeGridFireCell.pressureScore >
                    cityChallengeSettings.maximumAcceptedCellPressure)
            {
                return false;
            }

            Vector3 livePlayerPosition = playerBody != null
                ? playerBody.worldCenterOfMass
                : activeGridFireCell.worldSamplePosition;
            int sector = SelectAllowedPressureSector(
                activeGridFireCell,
                desiredKind,
                livePlayerPosition);
            if (sector < 0)
                return false;
            int matchCount = 0;
            for (int index = 0;
                 index < activeGridFireCell.fireWindows.Count;
                 index++)
            {
                EdpcgAirFireWindow window =
                    activeGridFireCell.fireWindows[index];
                if (IsRuntimeUsableFireWindow(window, desiredKind, sector,
                        livePlayerPosition))
                {
                    matchCount++;
                }
            }
            if (matchCount == 0)
                return false;
            int selected = PositiveModulo(
                enemy.RosterIndex * 31 + (int)intent * 7,
                matchCount);
            for (int index = 0;
                 index < activeGridFireCell.fireWindows.Count;
                 index++)
            {
                EdpcgAirFireWindow window =
                    activeGridFireCell.fireWindows[index];
                if (!IsRuntimeUsableFireWindow(
                        window, desiredKind, sector,
                        livePlayerPosition) || selected-- != 0)
                {
                    continue;
                }
                if (director == null || playerBody == null ||
                    !director.HasLineOfTravel(
                        window.routeEntryWorldPosition,
                        window.firingWorldPosition) ||
                    !director.HasLineOfTravel(
                        window.firingWorldPosition,
                        playerBody.worldCenterOfMass))
                {
                    continue;
                }
                destination = window.firingWorldPosition;
                areaId = activeGridFireCell.stableId;
                routeId = window.routeId;
                pendingGridRouteTargets[enemy.RosterMemberId] =
                    new PendingGridRouteTarget
                    {
                        stableId = window.stableId,
                        routeId = window.routeId,
                        segmentIndex = window.routeSegmentIndex,
                        segmentT = window.routeSegmentT,
                        routeEntryWorldPosition =
                            window.routeEntryWorldPosition,
                        destinationWorldPosition =
                            window.firingWorldPosition
                    };
                return true;
            }
            return false;
        }

        static bool IsRuntimeUsableFireWindow(
            EdpcgAirFireWindow window,
            EdpcgGridFireSourceKind kind,
            int sector,
            Vector3 livePlayerPosition)
        {
            return window != null && window.authorizedByTier &&
                   // Narrow building-gap anchors stay diagnostic until the
                   // real enemy movement controller validates turning,
                   // braking and loiter volume. Existing enemies may still
                   // naturally fire while passing a gap.
                   !window.throughBuildingGap &&
                   window.robustHullClear &&
                   window.navigationCorridorClear &&
                   window.aiPermissionCorridorClear &&
                   window.projectileCorridorClear &&
                   !string.IsNullOrEmpty(window.routeId) &&
                   window.routeSegmentIndex >= 0 &&
                   window.sourceKind == kind &&
                   window.azimuthSector == sector &&
                   Vector3.Distance(
                       window.firingWorldPosition,
                       livePlayerPosition) <= 420f &&
                   EdpcgThreatDirectionUtility.HorizontalSector(
                       livePlayerPosition,
                       window.firingWorldPosition) == sector;
        }

        int SelectAllowedPressureSector(
            EdpcgGridFireCell cell,
            EdpcgGridFireSourceKind kind,
            Vector3 livePlayerPosition)
        {
            int availableMask = 0;
            for (int index = 0; index < cell.fireWindows.Count; index++)
            {
                EdpcgAirFireWindow window = cell.fireWindows[index];
                if (window != null && window.authorizedByTier &&
                    !window.throughBuildingGap &&
                    window.robustHullClear &&
                    window.navigationCorridorClear &&
                    window.aiPermissionCorridorClear &&
                    window.projectileCorridorClear &&
                    !string.IsNullOrEmpty(window.routeId) &&
                    window.routeSegmentIndex >= 0 &&
                    window.sourceKind == kind &&
                    Vector3.Distance(window.firingWorldPosition,
                        livePlayerPosition) <= 420f)
                {
                    int liveSector =
                        EdpcgThreatDirectionUtility.HorizontalSector(
                            livePlayerPosition,
                            window.firingWorldPosition);
                    availableMask |= 1 << liveSector;
                }
            }
            if (availableMask == 0)
                return -1;

            int liveMask = pressureDirectionMask & availableMask;
            int maximumDirections = Mathf.Clamp(
                cityChallengeSettings.maximumPressureDirections, 1, 3);
            if (pressureDirectionCount >= maximumDirections)
                return FirstSector(liveMask);

            // Admit at most one new direction at a time. Every ranged enemy
            // asked during the same director snapshot receives that same
            // sector, preventing one frame from creating a 360-degree fan.
            int newMask = availableMask & ~pressureDirectionMask;
            int newSector = FirstSector(newMask);
            return newSector >= 0 ? newSector : FirstSector(liveMask);
        }

        static int FirstSector(int mask)
        {
            for (int sector = 0; sector < 8; sector++)
            {
                if ((mask & (1 << sector)) != 0)
                    return sector;
            }
            return -1;
        }

        static int PositiveModulo(int value, int modulus)
        {
            if (modulus <= 0)
                return 0;
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
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
            if (selected.hasTargetProgress)
            {
                return BuildTargetedRouteWaypoints(
                    selected, route, start, output);
            }
            int direction = route.AllowReverse
                ? (selected.direction < 0 ? -1 : 1)
                : 1;
            int waypointIndex = Mathf.Clamp(
                selected.waypointIndex,
                0,
                route.Points.Length - 1);
            if (direction > 0)
            {
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
            while (waypointIndex > 0 &&
                   Vector3.Distance(start, route.Points[waypointIndex]) < 18f)
            {
                waypointIndex--;
            }
            selected.waypointIndex = waypointIndex;
            for (int index = waypointIndex; index >= 0; index--)
            {
                output.Add(route.Points[index]);
            }
            return output.Count > 0;
        }

        static bool BuildTargetedRouteWaypoints(
            EdpcgRouteReservation reservation,
            EdpcgRuntimeRoute route,
            Vector3 start,
            List<Vector3> output)
        {
            int targetSegment = Mathf.Clamp(
                reservation.targetSegmentIndex,
                0,
                route.Points.Length - 2);
            float targetT = Mathf.Clamp01(reservation.targetSegmentT);
            Vector3 target = Vector3.Lerp(
                route.Points[targetSegment],
                route.Points[targetSegment + 1],
                targetT);
            ProjectRouteProgress(
                route.Points,
                start,
                out int currentSegment,
                out _,
                out float currentProgress);
            float targetProgress = RouteProgressDistance(
                route.Points, targetSegment, targetT);
            if (Mathf.Abs(targetProgress - currentProgress) <= 8f)
            {
                output.Add(target);
                reservation.waypointIndex = targetSegment;
                return true;
            }

            if (currentProgress < targetProgress)
            {
                int lastVertex = targetT <= 0.001f
                    ? targetSegment
                    : targetSegment + 1;
                for (int index = currentSegment + 1;
                     index <= lastVertex && index < route.Points.Length;
                     index++)
                {
                    if (index == targetSegment + 1 && targetT < 0.999f)
                        break;
                    output.Add(route.Points[index]);
                }
                if (output.Count == 0 ||
                    Vector3.Distance(output[output.Count - 1], target) > 0.5f)
                {
                    output.Add(target);
                }
                reservation.direction = 1;
                reservation.waypointIndex = targetSegment;
                return true;
            }

            for (int index = currentSegment;
                 index > targetSegment && index >= 0;
                 index--)
            {
                output.Add(route.Points[index]);
            }
            if (output.Count == 0 ||
                Vector3.Distance(output[output.Count - 1], target) > 0.5f)
            {
                output.Add(target);
            }
            reservation.direction = -1;
            reservation.waypointIndex = targetSegment + 1;
            return true;
        }

        static bool ReservationMatchesGridTarget(
            EdpcgRouteReservation reservation,
            PendingGridRouteTarget target)
        {
            if (reservation == null)
                return false;
            if (target == null)
                return !reservation.hasTargetProgress;
            return reservation.hasTargetProgress &&
                   reservation.targetSegmentIndex == target.segmentIndex &&
                   Mathf.Abs(reservation.targetSegmentT - target.segmentT) <=
                   0.001f &&
                   string.Equals(
                       reservation.targetStableId,
                       target.stableId,
                       StringComparison.Ordinal);
        }

        static float EstimateTargetedRouteSeconds(
            EdpcgRuntimeRoute route,
            Vector3 start,
            PendingGridRouteTarget target,
            float maximumSpeed,
            float minimumOccupancySeconds)
        {
            if (route?.Points == null || route.Points.Length < 2 ||
                target == null)
            {
                return route?.EstimatedTravelSeconds ?? 0.5f;
            }
            ProjectRouteProgress(
                route.Points, start, out _, out _, out float currentProgress);
            float targetProgress = RouteProgressDistance(
                route.Points, target.segmentIndex, target.segmentT);
            float routeDistance = Mathf.Abs(targetProgress - currentProgress);
            float offRouteDistance = Vector3.Distance(
                target.routeEntryWorldPosition,
                target.destinationWorldPosition);
            return Mathf.Max(
                Mathf.Max(0.25f, minimumOccupancySeconds + 0.35f),
                (routeDistance + offRouteDistance) /
                Mathf.Max(10f, maximumSpeed) + 0.5f);
        }

        static void ProjectRouteProgress(
            Vector3[] points,
            Vector3 position,
            out int segmentIndex,
            out float segmentT,
            out float progressDistance)
        {
            segmentIndex = 0;
            segmentT = 0f;
            progressDistance = 0f;
            if (points == null || points.Length < 2)
                return;
            float bestSqrDistance = float.PositiveInfinity;
            float cumulative = 0f;
            for (int index = 0; index < points.Length - 1; index++)
            {
                Vector3 segment = points[index + 1] - points[index];
                float length = segment.magnitude;
                float amount = length <= 0.001f
                    ? 0f
                    : Mathf.Clamp01(Vector3.Dot(
                        position - points[index], segment) /
                        (length * length));
                Vector3 projected = points[index] + segment * amount;
                float sqrDistance = (position - projected).sqrMagnitude;
                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    segmentIndex = index;
                    segmentT = amount;
                    progressDistance = cumulative + length * amount;
                }
                cumulative += length;
            }
        }

        static float RouteProgressDistance(
            Vector3[] points,
            int segmentIndex,
            float segmentT)
        {
            if (points == null || points.Length < 2)
                return 0f;
            int targetSegment = Mathf.Clamp(
                segmentIndex, 0, points.Length - 2);
            float distance = 0f;
            for (int index = 0; index < targetSegment; index++)
                distance += Vector3.Distance(points[index], points[index + 1]);
            distance += Vector3.Distance(
                points[targetSegment], points[targetSegment + 1]) *
                Mathf.Clamp01(segmentT);
            return distance;
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

        void UpdatePressureControl(float deltaTime)
        {
            if (settings == null || deltaTime <= 0f)
                return;
            settings.ResolveTargetBand(
                phase,
                out float targetMinimum,
                out float targetMaximum);
            float tolerance = settings.pressureTargetTolerance;
            int maximumAssist = phase == EdpcgEncounterPhase.Preview ||
                                phase == EdpcgEncounterPhase.Recover
                ? 1
                : settings.maximumPressureAssistSteps;
            float authoredStepSeconds = settings.pressureControlStepSeconds *
                                        (phase == EdpcgEncounterPhase.Preview
                                            ? 1.5f
                                            : 1f);
            float lowPressureStepSeconds =
                EdpcgFacilityAssaultPacingPolicy.LowPressureStepSeconds(
                    missionId,
                    authoredStepSeconds);

            if (smoothedPressure < targetMinimum - tolerance)
            {
                highPressureControlSeconds = 0f;
                pressureControlReleaseSeconds = 0f;
                lowPressureControlSeconds += deltaTime;
                while (lowPressureControlSeconds >=
                       lowPressureStepSeconds &&
                       (pressureBrakeLevel > 0 ||
                        pressureAssistLevel < maximumAssist))
                {
                    lowPressureControlSeconds -= lowPressureStepSeconds;
                    if (pressureBrakeLevel > 0)
                        pressureBrakeLevel--;
                    else
                        pressureAssistLevel++;
                }
                return;
            }

            lowPressureControlSeconds = 0f;
            if (smoothedPressure > targetMaximum + tolerance)
            {
                pressureControlReleaseSeconds = 0f;
                highPressureControlSeconds += deltaTime;
                while (highPressureControlSeconds >= authoredStepSeconds &&
                       (pressureAssistLevel > 0 || pressureBrakeLevel < 2))
                {
                    highPressureControlSeconds -= authoredStepSeconds;
                    if (pressureAssistLevel > 0)
                        pressureAssistLevel--;
                    else
                        pressureBrakeLevel++;
                }
                return;
            }

            highPressureControlSeconds = 0f;
            pressureControlReleaseSeconds += deltaTime;
            if (pressureControlReleaseSeconds <
                settings.pressureControlReleaseSeconds)
            {
                return;
            }
            pressureControlReleaseSeconds = 0f;
            if (pressureBrakeLevel > 0)
                pressureBrakeLevel--;
            else if (pressureAssistLevel > 0)
                pressureAssistLevel--;
        }

        void UpdatePressure(float deltaTime)
        {
            CountRosterStates(
                out int unspawned,
                out int queued,
                out int spawnRecovery,
                out int rosterActive,
                out int rosterNavigationRecovery);
            // Measure against the phase budget before assistance/braking.
            // Using the already-braked cap as a denominator creates a false
            // positive loop: brake -> smaller cap -> higher pressure -> brake.
            int dynamicPopulationCap = BasePopulationCapForPressure();
            int dynamicEngagementCap = BaseEngagementCapForPressure();
            int dynamicAttackTokenCap = BaseAttackTokenCapForPressure();
            float populationDenominator = Mathf.Max(1f, dynamicPopulationCap);
            float threatDenominator = Mathf.Max(
                1f,
                dynamicPopulationCap * 1.5f);
            float pursuingThreat = Mathf.Clamp01(
                pursuingThreatCount / Mathf.Max(1f, dynamicEngagementCap));
            float closeApproachThreat = Mathf.Clamp01(
                closeApproachThreatCount /
                Mathf.Max(1f, dynamicEngagementCap));
            float enemyThreat = Mathf.Clamp01(
                activeThreatCost / threatDenominator * 0.30f +
                pursuingThreat * 0.27f +
                closeApproachThreat * 0.18f +
                engagementCount /
                    Mathf.Max(1f, dynamicEngagementCap) * 0.10f +
                attackTokensUsed /
                    Mathf.Max(1f, dynamicAttackTokenCap) * 0.15f);
            float suicideNavigation = Mathf.Clamp01(
                suicideCommitCount /
                Mathf.Max(1f, settings.suicideCommitCap));
            float rangedNavigation = Mathf.Clamp01(
                rangedFireLaneCount /
                Mathf.Max(1f, settings.rangedFireLaneCap));
            float environmentalPursuit = Mathf.Clamp01(
                environmentalCommitments.Count /
                Mathf.Max(1f, dynamicEngagementCap));
            float navigation = Mathf.Clamp01(
                suicideNavigation * 0.42f +
                rangedNavigation * 0.42f +
                environmentalPursuit * 0.16f);
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

            // Observation-only pressure separates real combat exposure from
            // navigation/system failures. It deliberately does not feed any
            // spawn, token, strategy or trap-commit gate while Legacy remains
            // the active integration mode.
            float observedFire = rangedNavigation;
            float observedIntercept = suicideNavigation;
            float observedDisplacement = Mathf.Clamp01(
                pressureDirectionCount /
                Mathf.Max(1f, settings.pressureDirectionCap));
            float observedCombat = Mathf.Clamp01(
                observedFire * 0.45f +
                observedIntercept * 0.30f +
                observedDisplacement * 0.25f);
            float systemHealthIssue = Mathf.Clamp01(
                Mathf.Max(
                    navigationRecoveryCount,
                    rosterNavigationRecovery) /
                populationDenominator * 0.70f +
                spawnRecovery / Mathf.Max(1f, roster.Count) * 0.30f);
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
                        Mathf.Max(1f, dynamicAttackTokenCap),
                        environmentalPursuit)),
                environmentalPursuitPressure = environmentalPursuit,
                playerStrain = playerStrain,
                playerDamageAssist = Mathf.Clamp01(
                    Mathf.InverseLerp(0.55f, 0.85f, smoothedPressure) *
                    playerStrain),
                observedCombatPressure = observedCombat,
                observedFirePressure = observedFire,
                observedInterceptPressure = observedIntercept,
                observedDisplacementPressure = observedDisplacement,
                observedNetEnvironmentPressure = 0f,
                observedPlayerRisk = playerStrain,
                systemHealthIssueRatio = systemHealthIssue,
                pressureDirectionCount = pressureDirectionCount,
                pressureDirectionMask = pressureDirectionMask,
                environmentObservationAvailable = false,
                rosterCount = roster.Count,
                unspawnedCount = unspawned,
                queuedCount = queued,
                spawnRecoveryCount = spawnRecovery,
                activeCount = Mathf.Max(activeCount, rosterActive),
                engagementCount = engagementCount,
                attackTokensUsed = attackTokensUsed,
                suicideCommitCount = suicideCommitCount,
                rangedFireLaneCount = rangedFireLaneCount,
                pursuingThreatCount = pursuingThreatCount,
                closeApproachThreatCount = closeApproachThreatCount,
                navigationRecoveryCount = Mathf.Max(
                    navigationRecoveryCount,
                    rosterNavigationRecovery),
                environmentalPursuitCount =
                    environmentalCommitments.Count,
                pressureAssistLevel = pressureAssistLevel,
                pressureBrakeLevel = pressureBrakeLevel,
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

        void UpdateGridFireState()
        {
            if (playerBody == null)
                return;
            EdpcgGridFireAnalysis analysis = ResolveNearestGridFireLayer(
                playerBody.worldCenterOfMass.y);
            EdpcgGridFireCell candidate = analysis != null
                ? analysis.FindNearestCell(playerBody.worldCenterOfMass)
                : null;
            string candidateId = candidate != null
                ? analysis.altitudeLayer + ":" + candidate.stableId
                : string.Empty;
            if (!string.Equals(candidateId, gridCellCandidateId,
                    StringComparison.Ordinal))
            {
                gridCellCandidateId = candidateId;
                gridCellCandidateSince = elapsed;
            }
            activeGridFireAnalysis = analysis;
            activeGridFireCell = candidate;
        }

        EdpcgGridFireAnalysis ResolveNearestGridFireLayer(float playerWorldY)
        {
            EdpcgGridFireAnalysis best = null;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < gridFireAnalyses.Length; index++)
            {
                EdpcgGridFireAnalysis candidate = gridFireAnalyses[index];
                if (candidate == null || !candidate.IsUsable ||
                    candidate.cells.Count == 0)
                {
                    continue;
                }
                float distance = Mathf.Abs(
                    candidate.cells[0].worldSamplePosition.y - playerWorldY);
                if (distance >= bestDistance)
                    continue;
                best = candidate;
                bestDistance = distance;
            }
            return best;
        }

        static float ResolvePlayerShipWidth(Rigidbody body)
        {
            if (body == null)
                return 18f;
            Collider[] colliders = body.GetComponentsInChildren<Collider>(true);
            bool initialized = false;
            Bounds bounds = default(Bounds);
            for (int index = 0; index < colliders.Length; index++)
            {
                Collider collider = colliders[index];
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;
                if (!initialized)
                {
                    bounds = collider.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
            return initialized
                ? Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.z), 4f, 250f)
                : 18f;
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

    /// <summary>
    /// Pure mission overlay for the formal industrial-outpost encounter.
    /// Keeping this separate from EdpcgDifficultyProfile prevents one mission
    /// from rewriting the six shared difficulty tiers or their integration
    /// mode. The methods are deterministic and can be covered without a scene
    /// or a running HordeCombatDirector.
    /// </summary>
    public static class EdpcgFacilityAssaultPacingPolicy
    {
        public const string FormalMissionId = "industrial_outpost";
        public const float MaximumPreviewSeconds = 3f;
        public const float FacilitySpawnIntervalMultiplier = 0.72f;
        public const float FacilityLowPressureStepSeconds = 1.5f;
        public const float FacilityCloseApproachDistance = 320f;
        public const float ObjectiveAlertCooldownSeconds = 3f;
        public const float OpeningSeconds = 3f;
        public const int OpeningEnemyCount = 2;
        public const int MaximumSpawnUrgency = 2;

        public static bool Applies(string missionId)
        {
            return string.Equals(
                missionId,
                FormalMissionId,
                StringComparison.Ordinal);
        }

        public static void ApplySessionSettings(
            string missionId,
            EdpcgTierSettings sessionSettings)
        {
            if (!Applies(missionId) || sessionSettings == null)
                return;

            sessionSettings.previewSeconds = Mathf.Min(
                sessionSettings.previewSeconds,
                MaximumPreviewSeconds);
            sessionSettings.closeApproachDistance =
                FacilityCloseApproachDistance;
            // Deliberately do not touch integrationMode. The profile resolver
            // owns migration and the session already runs the existing pressure
            // controller regardless of its tactical-assignment presentation.
        }

        public static float SpawnIntervalMultiplier(string missionId)
        {
            return Applies(missionId)
                ? FacilitySpawnIntervalMultiplier
                : 1f;
        }

        public static float LowPressureStepSeconds(
            string missionId,
            float authoredStepSeconds)
        {
            float safeAuthored = Mathf.Max(0.01f, authoredStepSeconds);
            return Applies(missionId)
                ? Mathf.Min(safeAuthored, FacilityLowPressureStepSeconds)
                : safeAuthored;
        }

        public static float ResolveOpeningSeconds(
            string missionId,
            float authoredSeconds)
        {
            return Applies(missionId)
                ? OpeningSeconds
                : Mathf.Max(0f, authoredSeconds);
        }

        public static int ResolveOpeningEnemyCount(
            string missionId,
            int authoredCount)
        {
            return Applies(missionId)
                ? OpeningEnemyCount
                : Mathf.Max(0, authoredCount);
        }

        public static int ResolveThreatBudget(
            string missionId,
            int phase,
            int authoredBudget)
        {
            if (!Applies(missionId))
                return Mathf.Max(0, authoredBudget);
            switch (phase)
            {
                case 1:
                case 2:
                    return 4;
                case 3:
                    return 4;
                default:
                    return 2;
            }
        }

        public static int ResolveAssistFloor(
            int currentAssist,
            int requestedFloor,
            int maximumAssist)
        {
            int safeMaximum = Mathf.Max(0, maximumAssist);
            return Mathf.Clamp(
                Mathf.Max(currentAssist, requestedFloor),
                0,
                safeMaximum);
        }

        public static int MergeSpawnUrgency(
            int currentUrgency,
            int requestedUrgency)
        {
            return Mathf.Clamp(
                Mathf.Max(currentUrgency, requestedUrgency),
                0,
                MaximumSpawnUrgency);
        }
    }
}
