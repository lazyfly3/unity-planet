using System.Collections;
using UnityEngine;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.IcePlanet
{
    [System.Serializable]
    public struct IcePlanetFlightConditionSnapshot
    {
        public float temperatureCelsius;
        public float localAirDensityMultiplier;
        public float localWindSpeed;
        public float icingRisk;
        public float visibility;
        public bool lowGripSurfaceNearby;
    }

    /// <summary>
    /// Decorates the already-supported planet environment provider. It does
    /// not own the Rigidbody, change mass/inertia, or add forces itself. The
    /// existing RC3.2 motion owner consumes the modified wind and density via
    /// its public IPlanetEnvironmentProvider seam.
    /// </summary>
    [DefaultExecutionOrder(-240)]
    [DisallowMultipleComponent]
    public sealed class IcePlanetEnvironmentConditions :
        MonoBehaviour,
        IPlanetEnvironmentProvider,
        IPlanetAirEnvironmentProvider
    {
        IPlanetEnvironmentProvider source;
        IcePlanetCombatArea combatArea;
        RobocraftMotionCoordinator boundMotion;
        Rigidbody boundBody;
        string missionId;
        int seed;
        bool forceNoWind;
        float temperatureCelsius;
        float densityMultiplier;
        float windMultiplier;
        float crossWindSpeed;
        float additionalGustSpeed;
        float visibility;
        float icingRisk;

        public bool ForceNoWind
        {
            get => forceNoWind;
            set
            {
                forceNoWind = value;
                if (source != null)
                    source.ForceNoWind = value;
            }
        }

        public IcePlanetFlightConditionSnapshot Current { get; private set; }

        public void Configure(
            IPlanetEnvironmentProvider baseProvider,
            IcePlanetCombatArea area,
            string valueMissionId,
            int valueSeed)
        {
            source = ReferenceEquals(baseProvider, this)
                ? null
                : baseProvider;
            combatArea = area;
            missionId = valueMissionId ?? string.Empty;
            seed = valueSeed;
            ResolveProfile();
            StartCoroutine(BindToRestoredSpacecraft());
        }

        public PlanetEnvironmentSample Sample(
            Vector3 worldPosition,
            double simulationTime)
        {
            PlanetEnvironmentSample sample = source != null
                ? source.Sample(worldPosition, simulationTime)
                : PlanetEnvironmentSample.EarthLike(
                    Vector3.down * 9.81f,
                    Mathf.Max(0f, worldPosition.y));
            return ApplyLocalConditions(
                sample,
                worldPosition,
                simulationTime);
        }

        public PlanetEnvironmentSample SampleAirflow(
            Vector3 worldPosition,
            double simulationTime)
        {
            PlanetEnvironmentSample sample;
            if (source is IPlanetAirEnvironmentProvider airSource)
            {
                sample = airSource.SampleAirflow(
                    worldPosition,
                    simulationTime);
            }
            else
            {
                sample = source != null
                    ? source.Sample(worldPosition, simulationTime)
                    : PlanetEnvironmentSample.EarthLike(
                        Vector3.down * 9.81f,
                        Mathf.Max(0f, worldPosition.y));
            }
            return ApplyLocalConditions(
                sample,
                worldPosition,
                simulationTime);
        }

        PlanetEnvironmentSample ApplyLocalConditions(
            PlanetEnvironmentSample sample,
            Vector3 worldPosition,
            double simulationTime)
        {
            float influence = CalculateInfluence(worldPosition);
            if (influence <= 0.0001f || !sample.hasAtmosphere)
                return sample;

            float localDensityMultiplier = Mathf.Lerp(
                1f,
                densityMultiplier,
                influence);
            sample.airDensity *= localDensityMultiplier;
            if (forceNoWind)
            {
                sample.meanWindVelocity = Vector3.zero;
                sample.gustVelocity = Vector3.zero;
                sample.atmosphereVelocity = Vector3.zero;
                return sample;
            }

            Vector3 right = combatArea != null
                ? combatArea.transform.right
                : Vector3.right;
            Vector3 forward = combatArea != null
                ? combatArea.transform.forward
                : Vector3.forward;
            float time = (float)(simulationTime % 100000d);
            float phase = Hash01(seed, 17) * Mathf.PI * 2f;
            float primary = Mathf.Sin(time * 0.91f + phase);
            float secondary = Mathf.Sin(time * 1.73f + phase * 0.37f);
            float spatial = Mathf.PerlinNoise(
                    worldPosition.x * 0.012f + Hash01(seed, 29) * 11f,
                    worldPosition.z * 0.012f + time * 0.035f)
                * 2f - 1f;
            Vector3 localCrossWind = right * crossWindSpeed;
            Vector3 localGust = (
                    right * primary
                    + forward * secondary * 0.42f
                    + Vector3.up * spatial * 0.18f)
                * additionalGustSpeed;
            sample.meanWindVelocity =
                sample.meanWindVelocity
                    * Mathf.Lerp(1f, windMultiplier, influence)
                + localCrossWind * influence;
            sample.gustVelocity =
                sample.gustVelocity
                    * Mathf.Lerp(1f, windMultiplier, influence)
                + localGust * influence;
            sample.atmosphereVelocity =
                sample.meanWindVelocity + sample.gustVelocity;
            return sample;
        }

        IEnumerator BindToRestoredSpacecraft()
        {
            int remainingFrames = 1800;
            while (remainingFrames-- > 0)
            {
                if (source == null)
                {
                    PlanetEnvironmentProvider discovered =
                        FindObjectOfType<PlanetEnvironmentProvider>(true);
                    if (discovered != null)
                        source = discovered;
                }
                RobocraftMotionCoordinator motion =
                    FindObjectOfType<RobocraftMotionCoordinator>(true);
                if (motion != null && source != null)
                {
                    boundMotion = motion;
                    boundBody = motion.GetComponent<Rigidbody>();
                    boundMotion.SetEnvironmentProvider(this);
                    PlanetEnvironmentRuntime.Active = this;
                    yield break;
                }
                yield return null;
            }
            Debug.LogWarning(
                "[IcePlanet] The ice environment was generated, but no "
                + "RC3.2 spacecraft motion owner became available. "
                + "Visual conditions remain active; ship physics were not touched.",
                this);
        }

        void Update()
        {
            Vector3 samplePosition = boundBody != null
                ? boundBody.worldCenterOfMass
                : combatArea != null
                    ? combatArea.WorldCenter
                    : transform.position;
            PlanetEnvironmentSample sample = Sample(
                samplePosition,
                Time.timeAsDouble);
            float influence = CalculateInfluence(samplePosition);
            Current = new IcePlanetFlightConditionSnapshot
            {
                temperatureCelsius = temperatureCelsius,
                localAirDensityMultiplier = Mathf.Lerp(
                    1f,
                    densityMultiplier,
                    influence),
                localWindSpeed = sample.atmosphereVelocity.magnitude,
                icingRisk = icingRisk * influence,
                visibility = Mathf.Lerp(1f, visibility, influence),
                lowGripSurfaceNearby = influence > 0.2f
            };
        }

        void ResolveProfile()
        {
            switch (missionId)
            {
                case "wind_canyon":
                    temperatureCelsius = -34f;
                    densityMultiplier = 1.09f;
                    windMultiplier = 1.35f;
                    crossWindSpeed = 9f;
                    additionalGustSpeed = 7f;
                    visibility = 0.42f;
                    icingRisk = 0.86f;
                    break;
                case "industrial_outpost":
                    temperatureCelsius = -24f;
                    densityMultiplier = 1.05f;
                    windMultiplier = 1.15f;
                    crossWindSpeed = 4f;
                    additionalGustSpeed = 4f;
                    visibility = 0.62f;
                    icingRisk = 0.64f;
                    break;
                default:
                    temperatureCelsius = -28f;
                    densityMultiplier = 1.06f;
                    windMultiplier = 1.05f;
                    crossWindSpeed = 2.5f;
                    additionalGustSpeed = 2.8f;
                    visibility = 0.72f;
                    icingRisk = 0.52f;
                    break;
            }
        }

        float CalculateInfluence(Vector3 worldPosition)
        {
            if (combatArea == null)
                return 0f;
            Vector3 local = combatArea.transform.InverseTransformPoint(
                worldPosition);
            Vector2 half = combatArea.ArenaSize * 0.5f;
            float normalized = Mathf.Max(
                Mathf.Abs(local.x) / Mathf.Max(1f, half.x),
                Mathf.Abs(local.z) / Mathf.Max(1f, half.y));
            return 1f - Mathf.SmoothStep(0.78f, 1.28f, normalized);
        }

        void OnDisable()
        {
            if (boundMotion != null)
                boundMotion.SetEnvironmentProvider(source);
            if (ReferenceEquals(PlanetEnvironmentRuntime.Active, this))
                PlanetEnvironmentRuntime.Active = source;
        }

        static float Hash01(int value, int salt)
        {
            unchecked
            {
                uint hash = (uint)(value ^ (salt * 0x45d9f3b));
                hash ^= hash >> 16;
                hash *= 0x7feb352d;
                hash ^= hash >> 15;
                hash *= 0x846ca68b;
                hash ^= hash >> 16;
                return (hash & 0x00ffffff) / 16777215f;
            }
        }
    }
}
