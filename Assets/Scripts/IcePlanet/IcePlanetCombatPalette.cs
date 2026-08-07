using UnityEngine;

namespace UnityPlanet.IcePlanet
{
    [CreateAssetMenu(
        menuName = "Unity Planet/Ice Planet Combat Palette",
        fileName = "IcePlanetCombatPalette")]
    public sealed class IcePlanetCombatPalette : ScriptableObject
    {
        [SerializeField] GameObject[] boundaryCliffs;
        [SerializeField] GameObject[] coverRocks;
        [SerializeField] GameObject[] iceCrystals;
        [SerializeField] GameObject[] ruins;
        [SerializeField] GameObject[] walls;
        [SerializeField] GameObject[] towers;
        [SerializeField] GameObject[] bridges;
        [SerializeField] GameObject[] lights;
        [SerializeField] GameObject[] snowFx;

        public GameObject Pick(
            IcePlanetCombatVisualRole role,
            int deterministicIndex)
        {
            GameObject[] pool = GetPool(role);
            if (pool == null || pool.Length == 0)
                return null;
            int index = deterministicIndex % pool.Length;
            if (index < 0)
                index += pool.Length;
            for (int offset = 0; offset < pool.Length; offset++)
            {
                GameObject value = pool[(index + offset) % pool.Length];
                if (value != null)
                    return value;
            }
            return null;
        }

        GameObject[] GetPool(IcePlanetCombatVisualRole role)
        {
            switch (role)
            {
                case IcePlanetCombatVisualRole.BoundaryCliff:
                    return boundaryCliffs;
                case IcePlanetCombatVisualRole.CoverRock:
                    return coverRocks;
                case IcePlanetCombatVisualRole.IceCrystal:
                    return iceCrystals;
                case IcePlanetCombatVisualRole.Ruin:
                    return ruins;
                case IcePlanetCombatVisualRole.Wall:
                    return walls;
                case IcePlanetCombatVisualRole.Tower:
                    return towers;
                case IcePlanetCombatVisualRole.Bridge:
                    return bridges;
                case IcePlanetCombatVisualRole.IcePatch:
                    return null;
                case IcePlanetCombatVisualRole.Light:
                    return lights;
                case IcePlanetCombatVisualRole.SnowFx:
                    return snowFx;
                default:
                    return null;
            }
        }
    }
}
