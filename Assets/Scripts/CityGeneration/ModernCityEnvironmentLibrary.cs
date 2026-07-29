using System;
using UnityEngine;

namespace CityGeneration
{
    [Serializable]
    public sealed class ModernCityEnvironmentLibrary
    {
        [Header("Street Furniture")]
        public GameObject[] streetLights = Array.Empty<GameObject>();
        public GameObject[] trees = Array.Empty<GameObject>();
        public GameObject[] benches = Array.Empty<GameObject>();
        public GameObject[] busStops = Array.Empty<GameObject>();

        public bool IsConfigured =>
            HasAsset(streetLights)
            || HasAsset(trees)
            || HasAsset(benches)
            || HasAsset(busStops);

        public GameObject GetPrefab(
            CitySemanticPointType type,
            int stableIndex)
        {
            switch (type)
            {
                case CitySemanticPointType.StreetLight:
                    return Pick(streetLights, stableIndex);
                case CitySemanticPointType.Tree:
                    return Pick(trees, stableIndex);
                case CitySemanticPointType.Bench:
                    return Pick(benches, stableIndex);
                case CitySemanticPointType.BusStop:
                    return Pick(busStops, stableIndex);
                default:
                    return null;
            }
        }

        static GameObject Pick(
            GameObject[] values,
            int stableIndex)
        {
            if (values == null || values.Length == 0)
                return null;
            int start = Mathf.Abs(stableIndex) % values.Length;
            for (int offset = 0; offset < values.Length; offset++)
            {
                GameObject value =
                    values[(start + offset) % values.Length];
                if (value != null)
                    return value;
            }
            return null;
        }

        static bool HasAsset(GameObject[] values)
        {
            if (values == null)
                return false;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != null)
                    return true;
            }
            return false;
        }
    }
}
