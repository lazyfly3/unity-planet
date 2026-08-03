using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    [DisallowMultipleComponent]
    public sealed class VacuumEnvironmentProvider : MonoBehaviour,
        IPlanetEnvironmentProvider
    {
        public bool ForceNoWind
        {
            get => true;
            set { }
        }

        public PlanetEnvironmentSample Sample(
            Vector3 worldPosition,
            double simulationTime)
        {
            return new PlanetEnvironmentSample
            {
                gravityAcceleration = Vector3.zero,
                altitude = 0f,
                airDensity = 0f,
                ambientPressure = 0f,
                meanWindVelocity = Vector3.zero,
                gustVelocity = Vector3.zero,
                atmosphereVelocity = Vector3.zero,
                surfacePoint = Vector3.zero,
                surfaceNormal = Vector3.up,
                surfaceDistance = float.PositiveInfinity,
                hasSurface = false,
                hasAtmosphere = false
            };
        }
    }
}
