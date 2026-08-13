using System;
using UnityEngine;
using UnityPlanet.CityPcg;

namespace UnityPlanet.EDPCG
{
    public enum EdpcgCityTacticalPuzzleKind
    {
        SingleSidePressure,
        CrossfireBreak,
        CoverRelay,
        TrapLure,
        VerticalPressure,
        OpenShortcut
    }

    [Serializable]
    public sealed class EdpcgCityTacticalChallengeSettings
    {
        public EdpcgCityTacticalPuzzleKind puzzleKind =
            EdpcgCityTacticalPuzzleKind.CoverRelay;
        // Experimental and explicit opt-in. Anchors now carry route progress,
        // but live movement still needs Physics revalidation and does not yet
        // model the enemy controller's complete turning/braking trajectory.
        public bool enableOrdinaryRangedReposition;
        [Min(2f)] public float playerCellDwellSeconds = 5f;
        [Range(1, 3)] public int maximumPressureDirections = 2;
        [Range(1, 4)] public int minimumSafeExitCount = 2;
        [Range(0f, 1f)] public float maximumAcceptedCellPressure = 0.82f;
        [Range(0, 64)] public int minimumCrossfireCellCount = 4;
        [Range(0, 64)] public int maximumCrossfireCellCount = 24;
        public bool allowStrikerReposition = true;
        public bool allowGunshipReposition = true;

        public EdpcgCityTacticalChallengeSettings ValidatedCopy()
        {
            return new EdpcgCityTacticalChallengeSettings
            {
                puzzleKind = puzzleKind,
                enableOrdinaryRangedReposition =
                    enableOrdinaryRangedReposition,
                playerCellDwellSeconds = Mathf.Clamp(
                    playerCellDwellSeconds, 2f, 20f),
                maximumPressureDirections = Mathf.Clamp(
                    maximumPressureDirections, 1, 3),
                minimumSafeExitCount = Mathf.Clamp(
                    minimumSafeExitCount, 1, 4),
                maximumAcceptedCellPressure = Mathf.Clamp01(
                    maximumAcceptedCellPressure),
                minimumCrossfireCellCount = Mathf.Clamp(
                    minimumCrossfireCellCount, 0, 64),
                maximumCrossfireCellCount = Mathf.Clamp(
                    maximumCrossfireCellCount,
                    Mathf.Clamp(minimumCrossfireCellCount, 0, 64),
                    64),
                allowStrikerReposition = allowStrikerReposition,
                allowGunshipReposition = allowGunshipReposition
            };
        }
    }

    /// <summary>
    /// High-level authored contract that references, but does not merge, the
    /// city geometry and ordinary-enemy difficulty owners. CityPcg never
    /// references this type, keeping the dependency one-way.
    /// </summary>
    [CreateAssetMenu(
        fileName = "EdpcgCityTacticalChallengeProfile",
        menuName = "星球战斗/EDPCG/城市战术题目配置")]
    public sealed class EdpcgCityTacticalChallengeProfile : ScriptableObject
    {
        public const string ResourcePath =
            "EDPCG/EdpcgCityTacticalChallengeProfile";

        public CombatCityPcgDesignProfile cityGeometryProfile;
        public EdpcgDifficultyProfile ordinaryEnemyProfile;
        public EdpcgCityTacticalChallengeSettings[] tiers =
            Array.Empty<EdpcgCityTacticalChallengeSettings>();

        public void EnsureInitialized()
        {
            if (tiers == null || tiers.Length != 6)
            {
                var replacement = new EdpcgCityTacticalChallengeSettings[6];
                for (int index = 0; index < replacement.Length; index++)
                {
                    replacement[index] = tiers != null && index < tiers.Length &&
                                         tiers[index] != null
                        ? tiers[index]
                        : CreateDefaultTier(index);
                }
                tiers = replacement;
            }
            for (int index = 0; index < tiers.Length; index++)
            {
                if (tiers[index] == null)
                    tiers[index] = CreateDefaultTier(index);
                else
                    tiers[index] = tiers[index].ValidatedCopy();
            }
        }

        public EdpcgCityTacticalChallengeSettings Resolve(int zeroBasedTier)
        {
            EnsureInitialized();
            return tiers[Mathf.Clamp(zeroBasedTier, 0, 5)].ValidatedCopy();
        }

        public static EdpcgCityTacticalChallengeProfile
            LoadOrCreateMemoryDefault()
        {
            EdpcgCityTacticalChallengeProfile profile = Resources.Load<
                EdpcgCityTacticalChallengeProfile>(ResourcePath);
            if (profile != null)
            {
                profile.EnsureInitialized();
                return profile;
            }
            profile = CreateInstance<EdpcgCityTacticalChallengeProfile>();
            profile.name = "RuntimeEdpcgCityTacticalChallengeProfile";
            profile.EnsureInitialized();
            return profile;
        }

        static EdpcgCityTacticalChallengeSettings CreateDefaultTier(int tier)
        {
            EdpcgCityTacticalPuzzleKind[] rotation =
            {
                EdpcgCityTacticalPuzzleKind.SingleSidePressure,
                EdpcgCityTacticalPuzzleKind.CoverRelay,
                EdpcgCityTacticalPuzzleKind.OpenShortcut,
                EdpcgCityTacticalPuzzleKind.CrossfireBreak,
                EdpcgCityTacticalPuzzleKind.TrapLure,
                EdpcgCityTacticalPuzzleKind.VerticalPressure
            };
            return new EdpcgCityTacticalChallengeSettings
            {
                puzzleKind = rotation[Mathf.Clamp(tier, 0, 5)],
                enableOrdinaryRangedReposition = false,
                playerCellDwellSeconds = Mathf.Lerp(7.5f, 4.5f, tier / 5f),
                maximumPressureDirections = tier < 3 ? 1 : 2,
                minimumSafeExitCount = 2,
                maximumAcceptedCellPressure = Mathf.Lerp(
                    0.62f, 0.84f, tier / 5f),
                minimumCrossfireCellCount = tier < 2 ? 0 : 4 + tier * 2,
                maximumCrossfireCellCount = Mathf.Min(28, 12 + tier * 3),
                allowStrikerReposition = true,
                allowGunshipReposition = tier >= 1
            };
        }

        void OnValidate()
        {
            EnsureInitialized();
        }
    }
}
