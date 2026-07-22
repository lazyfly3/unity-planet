using UnityEngine;

namespace SpacecraftEditor
{
    [DisallowMultipleComponent]
    public sealed class ThrusterExhaustVfx : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float minimumActiveEmission = 0.65f;
        [SerializeField] private float idleSimulationSpeed = 0.65f;
        [SerializeField] private float fullSimulationSpeed = 1.25f;
        [SerializeField] private Transform plumeRoot;
        [SerializeField] private Vector3 plumeNozzleLocalPoint = new Vector3(1.73f, 0f, 0.03f);
        [SerializeField, Range(0.01f, 1f)] private float minimumPlumeLength = 0.18f;
        [SerializeField] private float maximumPlumeLength = 1.25f;
        [SerializeField, Range(0.01f, 1f)] private float minimumPlumeWidth = 0.72f;
        [SerializeField] private float maximumPlumeWidth = 1.05f;

        private ParticleSystem[] particleSystems;
        private float[] baseEmissionRates;
        private float[] baseDistanceRates;
        private float[] baseSimulationSpeeds;
        private Vector3 basePlumeScale;
        private Vector3 plumeAnchor;
        private bool initialized;

        public ParticleSystem PrimarySystem
        {
            get
            {
                Initialize();
                return particleSystems.Length == 0 ? null : particleSystems[0];
            }
        }

        private void Awake()
        {
            Initialize();
            SetThrottle(0f);
        }

        public void SetThrottle(float throttle)
        {
            Initialize();
            throttle = Mathf.Clamp01(throttle);
            bool shouldPlay = throttle > 0.001f;
            float emissionScale = shouldPlay
                ? Mathf.Lerp(minimumActiveEmission, 1f, throttle)
                : 0f;
            float simulationScale = Mathf.Lerp(idleSimulationSpeed, fullSimulationSpeed, throttle);
            UpdatePlumeSize(throttle);

            for (int index = 0; index < particleSystems.Length; index++)
            {
                ParticleSystem particles = particleSystems[index];
                if (particles == null)
                    continue;

                var emission = particles.emission;
                emission.rateOverTimeMultiplier = baseEmissionRates[index] * emissionScale;
                emission.rateOverDistanceMultiplier = baseDistanceRates[index] * emissionScale;

                var main = particles.main;
                main.simulationSpeed = baseSimulationSpeeds[index] * simulationScale;

                if (shouldPlay)
                {
                    if (!particles.isPlaying)
                        particles.Play(false);
                }
                else if (particles.isPlaying || particles.isEmitting)
                {
                    particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        private void Initialize()
        {
            if (initialized)
                return;

            particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            baseEmissionRates = new float[particleSystems.Length];
            baseDistanceRates = new float[particleSystems.Length];
            baseSimulationSpeeds = new float[particleSystems.Length];

            for (int index = 0; index < particleSystems.Length; index++)
            {
                ParticleSystem particles = particleSystems[index];
                var emission = particles.emission;
                var main = particles.main;
                baseEmissionRates[index] = emission.rateOverTimeMultiplier;
                baseDistanceRates[index] = emission.rateOverDistanceMultiplier;
                baseSimulationSpeeds[index] = Mathf.Max(0.01f, main.simulationSpeed);
            }

            if (plumeRoot == null)
                plumeRoot = transform.Find("UniqueThrusterVfx");
            if (plumeRoot != null)
            {
                basePlumeScale = plumeRoot.localScale;
                plumeAnchor = plumeRoot.localPosition
                    + plumeRoot.localRotation * Vector3.Scale(plumeNozzleLocalPoint, basePlumeScale);
            }

            initialized = true;
        }

        private void UpdatePlumeSize(float throttle)
        {
            if (plumeRoot == null)
                return;

            float shapedThrottle = Mathf.SmoothStep(0f, 1f, throttle);
            float lengthScale = Mathf.Lerp(minimumPlumeLength, maximumPlumeLength, shapedThrottle);
            float widthScale = Mathf.Lerp(minimumPlumeWidth, maximumPlumeWidth, shapedThrottle);
            Vector3 scaledSize = new Vector3(
                basePlumeScale.x * lengthScale,
                basePlumeScale.y * widthScale,
                basePlumeScale.z * widthScale);

            plumeRoot.localScale = scaledSize;
            plumeRoot.localPosition = plumeAnchor
                - plumeRoot.localRotation * Vector3.Scale(plumeNozzleLocalPoint, scaledSize);
        }

        private void OnDisable()
        {
            if (!initialized)
                return;

            for (int index = 0; index < particleSystems.Length; index++)
            {
                if (particleSystems[index] != null)
                    particleSystems[index].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }
}
