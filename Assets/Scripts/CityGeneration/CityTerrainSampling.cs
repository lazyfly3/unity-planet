using UnityEngine;

namespace CityGeneration
{
    public enum CitySurfaceKind
    {
        Land,
        River,
        Lake
    }

    public readonly struct CityTerrainSample
    {
        public readonly float GroundHeight;
        public readonly float SurfaceHeight;
        public readonly float WaterHeight;
        public readonly float WaterDepth;
        public readonly Vector3 Normal;
        public readonly float Grade;
        public readonly CitySurfaceKind SurfaceKind;

        public bool IsWater => SurfaceKind != CitySurfaceKind.Land;

        public CityTerrainSample(
            float groundHeight,
            float surfaceHeight,
            float waterHeight,
            float waterDepth,
            Vector3 normal,
            float grade,
            CitySurfaceKind surfaceKind)
        {
            GroundHeight = groundHeight;
            SurfaceHeight = surfaceHeight;
            WaterHeight = waterHeight;
            WaterDepth = waterDepth;
            Normal = normal.sqrMagnitude > 0.000001f
                ? normal.normalized
                : Vector3.up;
            Grade = Mathf.Max(0f, grade);
            SurfaceKind = surfaceKind;
        }

        public static CityTerrainSample Flat(float height = 0f)
        {
            return new CityTerrainSample(
                height,
                height,
                height,
                0f,
                Vector3.up,
                0f,
                CitySurfaceKind.Land);
        }
    }

    public interface ICityTerrainSampler
    {
        CityTerrainSample SampleTerrain(Vector2 worldXZ);
    }
}
