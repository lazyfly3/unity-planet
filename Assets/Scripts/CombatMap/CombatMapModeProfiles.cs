using UnityEngine;

namespace UnityPlanet.CombatMap
{
    /// <summary>
    /// Produces value-only mode profiles. The authored Recipe is never
    /// mutated, which keeps CombatMapLab authoring and existing saves stable.
    /// </summary>
    public static class CombatMapModeProfiles
    {
        public static AirCombatMapSettings Create(
            AirCombatMapSettings source,
            AirCombatMapMode mode)
        {
            AirCombatMapSettings result =
                (source ?? AirCombatMapSettings.CreateDefault())
                .ValidatedCopy();
            result.mode = mode;

            float clearanceTurnRadius = Mathf.Max(
                result.designTurnRadius * 1.15f,
                result.designTurnRadius + result.vehicleWingspan);
            float minimumSize = mode == AirCombatMapMode.Horde
                ? Mathf.Max(
                    clearanceTurnRadius * 11f,
                    result.designWeaponRange * 2.7f)
                : Mathf.Max(
                    clearanceTurnRadius * 12f,
                    result.designWeaponRange * 3f);
            float maximumSize = mode == AirCombatMapMode.Horde
                ? clearanceTurnRadius * 15f
                : clearanceTurnRadius * 16f;
            result.mapSize = Mathf.Clamp(
                result.mapSize,
                Mathf.Max(1024f, minimumSize),
                Mathf.Min(4096f, Mathf.Max(minimumSize, maximumSize)));

            if (mode == AirCombatMapMode.Horde)
            {
                result.theme = CombatMapTheme.Urban;
                result.spawnDistance = Mathf.Clamp(
                    clearanceTurnRadius * 4.4f,
                    result.mapSize * 0.30f,
                    result.mapSize * 0.42f);
                result.mainRouteWidth = Mathf.Max(
                    result.mainRouteWidth * 1.18f,
                    clearanceTurnRadius * 2.15f);
                result.canyonRouteWidth = Mathf.Max(
                    result.canyonRouteWidth * 1.12f,
                    clearanceTurnRadius * 1.65f);
                result.longRangeRouteWidth = Mathf.Max(
                    result.longRangeRouteWidth,
                    clearanceTurnRadius * 1.9f);
                result.mountainHeight *= 0.62f;
                // Urban districts are graded by semantic road and plot stamps,
                // so natural areas can keep layered relief without making
                // roads or foundations lumpy.
                result.microNoiseStrength *= 0.9f;
                // Eighteen graded tactical plots is the measured budget for
                // this arena scale. Allowing the authored legacy value (27)
                // to become twenty pads flattened enough mid-scale terrain to
                // create global high-eye positions. Skyline density is added
                // later as non-tactical outskirts dressing.
                result.occluderTowerCount = 18;
                result.targetOcclusionSeconds = Mathf.Min(
                    result.targetOcclusionSeconds,
                    4.25f);
                result.targetExposureSeconds = Mathf.Max(
                    result.targetExposureSeconds,
                    9f);
            }
            else
            {
                result.theme = result.theme == CombatMapTheme.Urban
                    ? CombatMapTheme.Ruins
                    : result.theme;
                result.occluderTowerCount = Mathf.Clamp(
                    result.occluderTowerCount,
                    8,
                    14);
            }

            result.Clamp();
            float maximumForfeit = result.MaximumSafeForfeitRadius;
            float desiredForfeit = mode == AirCombatMapMode.Horde
                ? result.mapSize * 0.415f
                : maximumForfeit;
            result.forfeitRadius = Mathf.Min(
                maximumForfeit,
                desiredForfeit);
            float warningGap = Mathf.Max(
                36f,
                result.designCombatSpeed * 0.75f);
            result.warningRadius = Mathf.Min(
                result.forfeitRadius - 20f,
                result.forfeitRadius - warningGap);
            result.Clamp();
            return result;
        }

        public static int SeedForMode(int baseSeed, AirCombatMapMode mode)
        {
            if (mode == AirCombatMapMode.Duel)
                return baseSeed;
            unchecked
            {
                uint value = (uint)baseSeed ^ 0xB5297A4Du;
                value ^= value >> 15;
                value *= 0x68E31DA4u;
                value ^= value >> 13;
                return (int)value;
            }
        }
    }
}
