using System;
using UnityEngine;

namespace UnityPlanet.EDPCG
{
    [Serializable]
    public sealed class EdpcgTierSettings
    {
        static readonly int[] BaseRosters = { 16, 24, 32, 40, 52, 64 };
        static readonly int[] DefaultEnvironmentalPursuers = { 2, 2, 3, 3, 4, 4 };
        [Header("运行模式")]
        public EdpcgIntegrationMode integrationMode =
            EdpcgIntegrationMode.Legacy;
        [Range(1, 6)] public int planetTier = 1;
        [Min(1)] public int rosterCount = 16;
        [Min(0)] public int interceptorCount = 11;
        [Min(0)] public int strikerCount = 4;
        [Min(0)] public int gunshipCount = 1;
        [Min(0)] public int environmentalPursuerCount;
        [Min(0)] public int requiredCreditedKills = 6;

        [Header("敌机耐久")]
        [Range(0.5f, 3f)] public float enemyHealthMultiplier = 1f;

        [Header("并发预算")]
        [Min(1)] public int populationCap = 8;
        [Min(1)] public int engagementCap = 5;
        [Min(1)] public int fullSimulationCap = 8;
        [Min(1)] public int attackTokenCap = 1;
        [Min(1)] public int suicideCommitCap = 1;
        [Min(1)] public int rangedFireLaneCap = 1;
        [Range(1, 3)] public int pressureDirectionCap = 1;

        [Header("节奏（秒）")]
        [Min(1f)] public float previewSeconds = 5f;
        [Min(1f)] public float engageSeconds = 18f;
        [Min(1f)] public float peakSeconds = 10f;
        [Min(1f)] public float recoverSeconds = 12f;
        [Min(0.25f)] public float spawnIntervalSeconds = 4.8f;
        [Min(0.1f)] public float sharedAttackCooldownSeconds = 0.65f;

        [Header("目标压力带")]
        [Range(0f, 1f)] public float previewPressureMin = 0.08f;
        [Range(0f, 1f)] public float previewPressureMax = 0.20f;
        [Range(0f, 1f)] public float engagePressureMin = 0.35f;
        [Range(0f, 1f)] public float engagePressureMax = 0.52f;
        [Range(0f, 1f)] public float peakPressureMin = 0.48f;
        [Range(0f, 1f)] public float peakPressureMax = 0.66f;
        [Range(0f, 1f)] public float recoverPressureMin = 0.12f;
        [Range(0f, 1f)] public float recoverPressureMax = 0.30f;
        [Range(0.55f, 0.9f)] public float hardPressureLimit = 0.75f;

        [Header("压力计算")]
        [Range(0f, 1f)] public float enemyThreatWeight = 0.45f;
        [Range(0f, 1f)] public float navigationWeight = 0.25f;
        [Range(0f, 1f)] public float environmentWeight = 0.14f;
        [Range(0f, 1f)] public float playerStrainWeight = 0.16f;
        [Range(0.25f, 5f)] public float pressureSmoothingSeconds = 1.5f;
        [Range(2f, 12f)] public float forecastSeconds = 8f;

        [Header("压力闭环")]
        [Range(1f, 6f)] public float pressureControlStepSeconds = 2.5f;
        [Range(2f, 8f)] public float pressureControlReleaseSeconds = 4f;
        [Range(1, 4)] public int maximumPressureAssistSteps = 3;
        [Range(0f, 0.1f)] public float pressureTargetTolerance = 0.03f;
        [Range(120f, 420f)] public float closeApproachDistance = 260f;

        [Header("AI 策略")]
        [Range(0, 3)] public int maximumStrategyLevel = 1;
        [Range(0.25f, 3f)] public float suicideTelegraphSeconds = 0.75f;
        [Range(0.5f, 3f)] public float suicideCommitSeconds = 1.25f;
        [Range(0.5f, 5f)] public float suicideBreakAwaySeconds = 2.2f;
        [Range(0.25f, 3f)] public float rangedTelegraphSeconds = 0.65f;
        [Range(0.25f, 3f)] public float rangedBurstSeconds = 0.8f;
        [Range(0.5f, 6f)] public float rangedRelocationSeconds = 1.8f;
        [Range(0.1f, 1f)] public float lineOfSightHysteresisSeconds = 0.35f;

        [Header("导航与恢复")]
        [Range(0.25f, 4f)] public float semanticReplanSeconds = 1.8f;
        [Range(1f, 4f)] public float localRepairAfterSeconds = 2f;
        [Range(3f, 7f)] public float evasiveAfterSeconds = 5f;
        [Range(6f, 12f)] public float navigationRecoveryAfterSeconds = 8f;
        [Range(2f, 6f)] public float reservationLeaseSeconds = 3f;
        [Range(5f, 15f)] public float repairSafeWindowSeconds = 10f;
        [Range(20f, 60f)] public float repairReuseCooldownSeconds = 40f;
        [Range(0.25f, 2f)] public float areaEnterDwellSeconds = 0.8f;
        [Range(0.5f, 3f)] public float areaExitDwellSeconds = 1.25f;

        public float CycleSeconds => Mathf.Max(
            4f,
            previewSeconds + engageSeconds + peakSeconds + recoverSeconds);

        public EdpcgTierSettings ValidatedCopy()
        {
            EdpcgTierSettings copy = (EdpcgTierSettings)MemberwiseClone();
            copy.ValidateInPlace();
            return copy;
        }

        public void ValidateInPlace()
        {
            if (!Enum.IsDefined(
                    typeof(EdpcgIntegrationMode),
                    integrationMode))
            {
                integrationMode = EdpcgIntegrationMode.Legacy;
            }
            planetTier = Mathf.Clamp(planetTier, 1, 6);
            rosterCount = Mathf.Max(1, rosterCount);
            interceptorCount = Mathf.Max(0, interceptorCount);
            strikerCount = Mathf.Max(0, strikerCount);
            gunshipCount = Mathf.Max(0, gunshipCount);
            NormalizeComposition();
            environmentalPursuerCount = Mathf.Clamp(
                environmentalPursuerCount,
                0,
                interceptorCount);
            requiredCreditedKills = Mathf.Clamp(
                requiredCreditedKills,
                0,
                Mathf.Max(0, strikerCount + gunshipCount));
            if (enemyHealthMultiplier <= 0f)
                enemyHealthMultiplier = 1f;
            enemyHealthMultiplier = Mathf.Clamp(
                enemyHealthMultiplier,
                0.5f,
                3f);

            populationCap = Mathf.Clamp(populationCap, 1, 28);
            engagementCap = Mathf.Clamp(engagementCap, 1, populationCap);
            fullSimulationCap = Mathf.Clamp(
                fullSimulationCap,
                1,
                Mathf.Min(16, populationCap));
            attackTokenCap = Mathf.Clamp(attackTokenCap, 1, 4);
            suicideCommitCap = Mathf.Clamp(
                suicideCommitCap,
                1,
                attackTokenCap);
            rangedFireLaneCap = Mathf.Clamp(
                rangedFireLaneCap,
                1,
                attackTokenCap);
            pressureDirectionCap = Mathf.Clamp(pressureDirectionCap, 1, 3);

            previewSeconds = Mathf.Max(1f, previewSeconds);
            engageSeconds = Mathf.Max(1f, engageSeconds);
            peakSeconds = Mathf.Clamp(peakSeconds, 1f, 12f);
            recoverSeconds = Mathf.Max(8f, recoverSeconds);
            spawnIntervalSeconds = Mathf.Max(0.25f, spawnIntervalSeconds);
            sharedAttackCooldownSeconds = Mathf.Max(
                0.1f,
                sharedAttackCooldownSeconds);

            ClampPressureBand(ref previewPressureMin, ref previewPressureMax);
            ClampPressureBand(ref engagePressureMin, ref engagePressureMax);
            ClampPressureBand(ref peakPressureMin, ref peakPressureMax);
            ClampPressureBand(ref recoverPressureMin, ref recoverPressureMax);
            hardPressureLimit = Mathf.Clamp(hardPressureLimit, 0.55f, 0.9f);
            peakPressureMax = Mathf.Min(peakPressureMax, hardPressureLimit);

            float weight = enemyThreatWeight + navigationWeight +
                           environmentWeight + playerStrainWeight;
            if (weight <= 0.001f)
            {
                enemyThreatWeight = 0.45f;
                navigationWeight = 0.25f;
                environmentWeight = 0.14f;
                playerStrainWeight = 0.16f;
            }
            else
            {
                enemyThreatWeight /= weight;
                navigationWeight /= weight;
                environmentWeight /= weight;
                playerStrainWeight /= weight;
            }

            pressureSmoothingSeconds = Mathf.Clamp(
                pressureSmoothingSeconds,
                0.25f,
                5f);
            forecastSeconds = Mathf.Clamp(forecastSeconds, 2f, 12f);
            pressureControlStepSeconds = Mathf.Clamp(
                pressureControlStepSeconds <= 0f
                    ? 2.5f
                    : pressureControlStepSeconds,
                1f,
                6f);
            pressureControlReleaseSeconds = Mathf.Clamp(
                pressureControlReleaseSeconds <= 0f
                    ? 4f
                    : pressureControlReleaseSeconds,
                2f,
                8f);
            maximumPressureAssistSteps = Mathf.Clamp(
                maximumPressureAssistSteps <= 0
                    ? 3
                    : maximumPressureAssistSteps,
                1,
                4);
            pressureTargetTolerance = Mathf.Clamp(
                pressureTargetTolerance <= 0f
                    ? 0.03f
                    : pressureTargetTolerance,
                0f,
                0.1f);
            closeApproachDistance = Mathf.Clamp(
                closeApproachDistance <= 0f
                    ? 260f
                    : closeApproachDistance,
                120f,
                420f);
            maximumStrategyLevel = Mathf.Clamp(maximumStrategyLevel, 0, 3);
            localRepairAfterSeconds = Mathf.Max(1f, localRepairAfterSeconds);
            evasiveAfterSeconds = Mathf.Max(
                localRepairAfterSeconds + 1f,
                evasiveAfterSeconds);
            navigationRecoveryAfterSeconds = Mathf.Max(
                evasiveAfterSeconds + 1f,
                navigationRecoveryAfterSeconds);
            repairSafeWindowSeconds = Mathf.Max(10f, repairSafeWindowSeconds);
            repairReuseCooldownSeconds = Mathf.Clamp(
                repairReuseCooldownSeconds,
                35f,
                45f);
        }

        public void ResolveTargetBand(
            EdpcgEncounterPhase phase,
            out float minimum,
            out float maximum)
        {
            switch (phase)
            {
                case EdpcgEncounterPhase.Preview:
                    minimum = previewPressureMin;
                    maximum = previewPressureMax;
                    break;
                case EdpcgEncounterPhase.Peak:
                    minimum = peakPressureMin;
                    maximum = peakPressureMax;
                    break;
                case EdpcgEncounterPhase.Recover:
                    minimum = recoverPressureMin;
                    maximum = recoverPressureMax;
                    break;
                default:
                    minimum = engagePressureMin;
                    maximum = engagePressureMax;
                    break;
            }
        }

        public EdpcgEncounterPhase ResolvePhase(
            float elapsed,
            out float phaseElapsed,
            out float phaseRemaining)
        {
            float time = Mathf.Repeat(Mathf.Max(0f, elapsed), CycleSeconds);
            if (time < previewSeconds)
            {
                phaseElapsed = time;
                phaseRemaining = previewSeconds - time;
                return EdpcgEncounterPhase.Preview;
            }
            time -= previewSeconds;
            if (time < engageSeconds)
            {
                phaseElapsed = time;
                phaseRemaining = engageSeconds - time;
                return EdpcgEncounterPhase.Engage;
            }
            time -= engageSeconds;
            if (time < peakSeconds)
            {
                phaseElapsed = time;
                phaseRemaining = peakSeconds - time;
                return EdpcgEncounterPhase.Peak;
            }
            time -= peakSeconds;
            phaseElapsed = time;
            phaseRemaining = Mathf.Max(0f, recoverSeconds - time);
            return EdpcgEncounterPhase.Recover;
        }

        void NormalizeComposition()
        {
            int sum = interceptorCount + strikerCount + gunshipCount;
            if (sum == rosterCount)
                return;
            if (sum <= 0)
            {
                interceptorCount = rosterCount;
                strikerCount = 0;
                gunshipCount = 0;
                return;
            }

            float scale = rosterCount / (float)sum;
            interceptorCount = Mathf.RoundToInt(interceptorCount * scale);
            strikerCount = Mathf.RoundToInt(strikerCount * scale);
            gunshipCount = Mathf.Clamp(
                rosterCount - interceptorCount - strikerCount,
                0,
                rosterCount);
            int corrected = interceptorCount + strikerCount + gunshipCount;
            interceptorCount = Mathf.Max(0, interceptorCount + rosterCount - corrected);
        }

        static void ClampPressureBand(ref float minimum, ref float maximum)
        {
            minimum = Mathf.Clamp01(minimum);
            maximum = Mathf.Clamp01(Mathf.Max(minimum, maximum));
        }

        public static EdpcgTierSettings CreateDefault(int zeroBasedTier)
        {
            int tier = Mathf.Clamp(zeroBasedTier, 0, 5);
            int[] interceptors = { 2, 4, 5, 7, 9, 12 };
            int[] strikers = { 12, 17, 23, 27, 35, 42 };
            int[] gunships = { 2, 3, 4, 6, 8, 10 };
            int[] credited = { 4, 6, 8, 10, 14, 20 };
            int[] population = { 8, 10, 14, 18, 22, 28 };
            int[] engagement = { 5, 7, 9, 11, 14, 16 };
            int[] tokens = { 2, 2, 2, 3, 3, 4 };
            int[] rangedLanes = { 1, 2, 2, 3, 3, 4 };
            float t = tier / 5f;
            var settings = new EdpcgTierSettings
            {
                integrationMode = EdpcgIntegrationMode.TacticalAssignments,
                planetTier = tier + 1,
                rosterCount = BaseRosters[tier] +
                              DefaultEnvironmentalPursuers[tier],
                interceptorCount = interceptors[tier] +
                                   DefaultEnvironmentalPursuers[tier],
                strikerCount = strikers[tier],
                gunshipCount = gunships[tier],
                environmentalPursuerCount =
                    DefaultEnvironmentalPursuers[tier],
                requiredCreditedKills = credited[tier],
                enemyHealthMultiplier = DefaultEnemyHealthMultiplierForTier(
                    tier),
                populationCap = population[tier],
                engagementCap = engagement[tier],
                fullSimulationCap = Mathf.Min(16, population[tier]),
                attackTokenCap = tokens[tier],
                suicideCommitCap = tier < 3 ? 1 : 2,
                rangedFireLaneCap = rangedLanes[tier],
                pressureDirectionCap = tier < 2 ? 1 : tier < 5 ? 2 : 3,
                spawnIntervalSeconds = Mathf.Lerp(5.2f, 2.6f, t),
                sharedAttackCooldownSeconds = Mathf.Lerp(0.8f, 0.42f, t),
                previewPressureMin = Mathf.Lerp(0.06f, 0.12f, t),
                previewPressureMax = Mathf.Lerp(0.16f, 0.24f, t),
                engagePressureMin = Mathf.Lerp(0.28f, 0.45f, t),
                engagePressureMax = Mathf.Lerp(0.44f, 0.62f, t),
                peakPressureMin = Mathf.Lerp(0.40f, 0.56f, t),
                peakPressureMax = Mathf.Lerp(0.58f, 0.74f, t),
                recoverPressureMin = Mathf.Lerp(0.10f, 0.16f, t),
                recoverPressureMax = Mathf.Lerp(0.26f, 0.34f, t),
                maximumStrategyLevel = tier < 2 ? 1 : tier < 4 ? 2 : 3,
                repairReuseCooldownSeconds = Mathf.Lerp(35f, 45f, t)
            };
            settings.ValidateInPlace();
            return settings;
        }

        public static int DefaultRosterCountForTier(int zeroBasedTier)
        {
            int tier = Mathf.Clamp(
                zeroBasedTier,
                0,
                BaseRosters.Length - 1);
            return BaseRosters[tier] + DefaultEnvironmentalPursuers[tier];
        }

        public static float DefaultEnemyHealthMultiplierForTier(
            int zeroBasedTier)
        {
            int tier = Mathf.Clamp(zeroBasedTier, 0, 5);
            return Mathf.Lerp(1.2f, 1.7f, tier / 5f);
        }
    }

    [CreateAssetMenu(
        fileName = "EdpcgDifficultyProfile",
        menuName = "Planet Combat/EDPCG Difficulty Profile")]
    public sealed class EdpcgDifficultyProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 6;
        public const string ResourcePath = "EDPCG/EdpcgDifficultyProfile";

        public int schemaVersion = CurrentSchemaVersion;
        public string profileId = "edpcg-default";
        public string profileVersion = "1.5.0";
        public string formulaVersion =
            "edpcg-pressure-v3-combat-calibrated-control";
        public EdpcgTierSettings[] tiers = Array.Empty<EdpcgTierSettings>();

        public void EnsureInitialized()
        {
            bool migrateEnvironmentalPursuers = schemaVersion < 2;
            bool migrateEnemyHealth = schemaVersion < 3;
            bool migratePressureControl = schemaVersion < 4;
            bool migrateRangedMajority = schemaVersion < 5;
            bool migrateCombatPressureWeights = schemaVersion < 6;
            if (tiers == null || tiers.Length != 6)
                Array.Resize(ref tiers, 6);
            for (int index = 0; index < tiers.Length; index++)
            {
                if (tiers[index] == null)
                {
                    tiers[index] = EdpcgTierSettings.CreateDefault(index);
                }
                else if (migrateEnvironmentalPursuers &&
                         tiers[index].environmentalPursuerCount <= 0)
                {
                    int addition = index < 2 ? 2 : index < 4 ? 3 : 4;
                    tiers[index].rosterCount += addition;
                    tiers[index].interceptorCount += addition;
                    tiers[index].environmentalPursuerCount = addition;
                }
                if (migrateEnemyHealth ||
                    tiers[index].enemyHealthMultiplier <= 0f)
                {
                    tiers[index].enemyHealthMultiplier =
                        EdpcgTierSettings.DefaultEnemyHealthMultiplierForTier(
                            index);
                }
                if (migratePressureControl)
                {
                    tiers[index].pressureControlStepSeconds = 2.5f;
                    tiers[index].pressureControlReleaseSeconds = 4f;
                    tiers[index].maximumPressureAssistSteps = 3;
                    tiers[index].pressureTargetTolerance = 0.03f;
                    tiers[index].closeApproachDistance = 260f;
                }
                if (migrateRangedMajority)
                {
                    ApplyRangedMajorityComposition(tiers[index], index);
                }
                if (migrateCombatPressureWeights)
                {
                    tiers[index].enemyThreatWeight = 0.45f;
                    tiers[index].navigationWeight = 0.25f;
                    tiers[index].environmentWeight = 0.14f;
                    tiers[index].playerStrainWeight = 0.16f;
                }
                tiers[index].planetTier = index + 1;
                tiers[index].ValidateInPlace();
            }
            schemaVersion = CurrentSchemaVersion;
            profileVersion = "1.5.0";
            formulaVersion = "edpcg-pressure-v3-combat-calibrated-control";
        }

        static void ApplyRangedMajorityComposition(
            EdpcgTierSettings settings,
            int zeroBasedTier)
        {
            if (settings == null)
                return;
            int tier = Mathf.Clamp(zeroBasedTier, 0, 5);
            int[] interceptors = { 4, 6, 8, 10, 13, 16 };
            int[] strikers = { 12, 17, 23, 27, 35, 42 };
            int[] gunships = { 2, 3, 4, 6, 8, 10 };
            int[] rangedBudgets = { 2, 2, 2, 3, 3, 4 };
            int[] rangedLanes = { 1, 2, 2, 3, 3, 4 };
            settings.NormalizeForFixedRoster(
                interceptors[tier],
                strikers[tier],
                gunships[tier]);
            settings.attackTokenCap = Mathf.Max(
                settings.attackTokenCap,
                rangedBudgets[tier]);
            settings.rangedFireLaneCap = Mathf.Max(
                settings.rangedFireLaneCap,
                rangedLanes[tier]);
        }

        public EdpcgTierSettings Resolve(int zeroBasedTier)
        {
            EnsureInitialized();
            return tiers[Mathf.Clamp(zeroBasedTier, 0, 5)].ValidatedCopy();
        }

        public static EdpcgDifficultyProfile LoadOrCreateMemoryDefault()
        {
            EdpcgDifficultyProfile profile = Resources.Load<
                EdpcgDifficultyProfile>(ResourcePath);
            if (profile != null)
            {
                profile.EnsureInitialized();
                return profile;
            }
            profile = CreateInstance<EdpcgDifficultyProfile>();
            profile.name = "RuntimeEdpcgDifficultyProfile";
            profile.EnsureInitialized();
            return profile;
        }

        void OnValidate()
        {
            EnsureInitialized();
        }
    }

    public static class EdpcgTierSettingsExtensions
    {
        public static void NormalizeForFixedRoster(
            this EdpcgTierSettings settings,
            int interceptors,
            int strikers,
            int gunships)
        {
            if (settings == null)
                return;
            settings.interceptorCount = Mathf.Max(0, interceptors);
            settings.strikerCount = Mathf.Max(0, strikers);
            settings.gunshipCount = Mathf.Max(0, gunships);
        }
    }
}
