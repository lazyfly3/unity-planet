using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    /// <summary>
    /// Session-only combat tuning used by the Editor enemy-damage tool.
    /// Damage is scaled at impact time so changes also affect projectiles that
    /// were already in flight when a slider moved.
    /// </summary>
    public static class EnemyDamageRuntimeTuning
    {
        public const float MinimumMultiplier = 0f;
        public const float MaximumMultiplier = 10f;

        static float globalMultiplier = 1f;
        static float rangedMultiplier = 1f;
        static float suicideMultiplier = 1f;

        public static float GlobalMultiplier => globalMultiplier;
        public static float RangedMultiplier => rangedMultiplier;
        public static float SuicideMultiplier => suicideMultiplier;

        public static void Configure(
            float global,
            float ranged,
            float suicide)
        {
            globalMultiplier = ClampMultiplier(global);
            rangedMultiplier = ClampMultiplier(ranged);
            suicideMultiplier = ClampMultiplier(suicide);
        }

        public static float ScaleProjectileDamage(
            GameObject source,
            float baseDamage)
        {
            return IsEnemySource(source)
                ? Mathf.Max(0f, baseDamage) *
                  globalMultiplier *
                  rangedMultiplier
                : baseDamage;
        }

        public static float ScaleExplosionDamage(
            GameObject source,
            float baseDamage)
        {
            if (!IsEnemySource(source))
                return baseDamage;

            HordeEnemyVehicle hordeEnemy = source == null
                ? null
                : source.GetComponentInParent<HordeEnemyVehicle>();
            float channelMultiplier = hordeEnemy != null &&
                                      hordeEnemy.IsSuicide
                ? suicideMultiplier
                : rangedMultiplier;
            return Mathf.Max(0f, baseDamage) *
                   globalMultiplier *
                   channelMultiplier;
        }

        static bool IsEnemySource(GameObject source)
        {
            return source != null &&
                   VehicleCombatTeamUtility.Resolve(source.transform) ==
                   VehicleCombatTeam.Enemy;
        }

        static float ClampMultiplier(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 1f;
            return Mathf.Clamp(
                value,
                MinimumMultiplier,
                MaximumMultiplier);
        }
    }
}
