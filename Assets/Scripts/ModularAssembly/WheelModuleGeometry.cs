using System;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    [Serializable]
    public struct WheelTyreGeometry
    {
        public Vector3 centerLocal;
        public float radius;
        public float width;

        public WheelTyreGeometry(
            Vector3 center,
            float tyreRadius,
            float tyreWidth)
        {
            centerLocal = center;
            radius = tyreRadius;
            width = tyreWidth;
        }
    }

    /// <summary>
    /// Semantic tyre geometry measured from the production AssetBundle meshes.
    /// The 4x2x2 wheel_l_422 is intentionally absent: it remains on the legacy
    /// single-envelope path until that asset is explicitly supported.
    /// </summary>
    public static class WheelModuleGeometryCatalog
    {
        private static readonly WheelTyreGeometry[] Basic =
        {
            new WheelTyreGeometry(
                new Vector3(0.2070303f, -0.1057799f, 0f),
                0.3990774f,
                0.4337587f)
        };

        private static readonly WheelTyreGeometry[] Medium =
        {
            new WheelTyreGeometry(
                new Vector3(0.3084659f, -0.2324381f, 0f),
                0.7797695f,
                0.8671514f)
        };

        private static readonly WheelTyreGeometry[] RacingFrontLeft =
        {
            new WheelTyreGeometry(
                new Vector3(
                    -0.4267096f,
                    -0.1431745f,
                    -0.0014590f),
                0.5419834f,
                0.5624151f)
        };

        private static readonly WheelTyreGeometry[] RacingFrontRight =
        {
            new WheelTyreGeometry(
                new Vector3(
                    0.4267096f,
                    -0.1431745f,
                    -0.0014590f),
                0.5419834f,
                0.5624151f)
        };

        private static readonly WheelTyreGeometry[] RacingRearLeft =
        {
            new WheelTyreGeometry(
                new Vector3(
                    -0.0110487f,
                    -0.2627589f,
                    -0.9597507f),
                0.7343171f,
                1.0092570f),
            new WheelTyreGeometry(
                new Vector3(
                    -0.0110488f,
                    -0.2656147f,
                    0.4424138f),
                0.7343853f,
                1.0092570f)
        };

        private static readonly WheelTyreGeometry[] RacingRearRight =
        {
            new WheelTyreGeometry(
                new Vector3(
                    0.0110487f,
                    -0.2627589f,
                    -0.9597507f),
                0.7343171f,
                1.0092570f),
            new WheelTyreGeometry(
                new Vector3(
                    0.0110488f,
                    -0.2656147f,
                    0.4424138f),
                0.7343853f,
                1.0092570f)
        };

        public static bool TryResolve(
            string neoXId,
            out WheelTyreGeometry[] tyres)
        {
            string id = neoXId ?? string.Empty;
            WheelTyreGeometry[] source;
            if (Contains(id, "speedwheel_large_l_522"))
                source = RacingRearLeft;
            else if (Contains(id, "speedwheel_large_r_522"))
                source = RacingRearRight;
            else if (Contains(id, "speedwheel_small_l_322"))
                source = RacingFrontLeft;
            else if (Contains(id, "speedwheel_small_r_322"))
                source = RacingFrontRight;
            else if (Contains(id, "wheel_basic_111"))
                source = Basic;
            else if (Contains(id, "wheel_m_222"))
                source = Medium;
            else
            {
                tyres = Array.Empty<WheelTyreGeometry>();
                return false;
            }

            tyres = (WheelTyreGeometry[])source.Clone();
            return true;
        }

        private static bool Contains(string value, string term)
        {
            return value.IndexOf(
                       term,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
