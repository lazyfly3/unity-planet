using UnityEngine;

namespace SpacecraftEditor
{
    [DisallowMultipleComponent]
    public sealed class ThrusterPart : SpacecraftPart
    {
        [SerializeField] private KeyCode activationKey = KeyCode.None;
        [SerializeField] private ThrusterExhaustVfx exhaustVfx;
        [SerializeField] private ParticleSystem exhaustParticles;
        [SerializeField] private Light exhaustLight;
        [SerializeField] private float baseEmissionRate = 42f;
        [SerializeField] private float baseParticleSpeed = 3.8f;

        private float currentThrottle;

        public KeyCode ActivationKey => activationKey;
        public bool IsBound => activationKey != KeyCode.None;
        public float ActualThrust => Definition == null ? 0f : Definition.BaseThrust * UniformScale * UniformScale;
        /// <summary>Direction in which reaction mass and exhaust leave the nozzle.</summary>
        public override Vector3 ExhaustDirection => transform.forward;
        /// <summary>Direction of the force applied to the spacecraft.</summary>
        public override Vector3 ThrustDirection => -ExhaustDirection;
        /// <summary>Compatibility alias. Prefer <see cref="ThrustDirection"/> in new code.</summary>
        public Vector3 ForceDirection => ThrustDirection;
        public Vector3 ExhaustOrigin => exhaustParticles == null ? transform.position : exhaustParticles.transform.position;
        public float CurrentThrottle => currentThrottle;

        protected override void Awake()
        {
            base.Awake();
            if (exhaustVfx == null)
                exhaustVfx = GetComponentInChildren<ThrusterExhaustVfx>(true);
            if (exhaustParticles == null)
                exhaustParticles = exhaustVfx == null
                    ? GetComponentInChildren<ParticleSystem>(true)
                    : exhaustVfx.PrimarySystem;
            if (exhaustLight == null)
                exhaustLight = GetComponentInChildren<Light>(true);
            SetExhaust(0f);
        }

        public override void Configure(
            ShipPartDefinition partDefinition,
            float scale,
            string id = null,
            string groupId = null,
            KeyCode key = KeyCode.None,
            string paintId = null,
            int weaponGroup = 0)
        {
            base.Configure(partDefinition, scale, id, groupId, key, paintId, weaponGroup);
            activationKey = key;
            if (exhaustVfx == null)
                exhaustVfx = GetComponentInChildren<ThrusterExhaustVfx>(true);
            if (exhaustParticles == null)
                exhaustParticles = exhaustVfx == null
                    ? GetComponentInChildren<ParticleSystem>(true)
                    : exhaustVfx.PrimarySystem;
            if (exhaustLight == null)
                exhaustLight = GetComponentInChildren<Light>(true);
            SetExhaust(0f);
        }

        public void SetActivationKey(KeyCode key)
        {
            activationKey = key;
        }

        public void ApplyThrust(Rigidbody body, float throttle)
        {
            ApplyThrust(body, throttle, 1f);
        }

        public void ApplyThrust(Rigidbody body, float throttle, float thrustMultiplier)
        {
            currentThrottle = Mathf.Clamp01(throttle);
            if (body != null && currentThrottle > 0.0001f)
            {
                float multiplier = Mathf.Max(1f, thrustMultiplier);
                body.AddForceAtPosition(
                    ThrustDirection * (ActualThrust * multiplier * currentThrottle),
                    transform.position,
                    ForceMode.Force);
            }
            SetExhaust(currentThrottle);
        }

        public void SetExhaust(float throttle)
        {
            if (exhaustVfx == null)
                exhaustVfx = GetComponentInChildren<ThrusterExhaustVfx>(true);
            if (exhaustParticles == null)
                exhaustParticles = exhaustVfx == null
                    ? GetComponentInChildren<ParticleSystem>(true)
                    : exhaustVfx.PrimarySystem;
            if (exhaustLight == null)
                exhaustLight = GetComponentInChildren<Light>(true);

            currentThrottle = Mathf.Clamp01(throttle);
            if (exhaustVfx != null)
                exhaustVfx.SetThrottle(currentThrottle);

            // Keep the original core plume as the bright nozzle center while the
            // layered VFX controller drives the imported surrounding effects.
            if (exhaustParticles != null)
            {
                var emission = exhaustParticles.emission;
                emission.rateOverTime = baseEmissionRate * UniformScale * UniformScale * currentThrottle;
                var main = exhaustParticles.main;
                main.startSpeed = baseParticleSpeed * UniformScale * Mathf.Lerp(0.45f, 1f, currentThrottle);
                main.startSize = new ParticleSystem.MinMaxCurve(0.07f * UniformScale, 0.18f * UniformScale);
                if (currentThrottle > 0.001f && !exhaustParticles.isPlaying)
                    exhaustParticles.Play(true);
                else if (currentThrottle <= 0.001f && exhaustParticles.isPlaying)
                    exhaustParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (exhaustLight != null)
            {
                exhaustLight.enabled = currentThrottle > 0.01f;
                exhaustLight.intensity = 1.4f * currentThrottle * UniformScale;
                exhaustLight.range = 2.2f * UniformScale;
            }
        }

        public override PlacedPartState CaptureState()
        {
            var state = base.CaptureState();
            state.activationKey = activationKey;
            return state;
        }

        private void OnDisable()
        {
            SetExhaust(0f);
        }
    }
}
