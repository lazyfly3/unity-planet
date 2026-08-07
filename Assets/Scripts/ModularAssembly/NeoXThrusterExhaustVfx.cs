using System;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    [DisallowMultipleComponent]
    public sealed class NeoXThrusterExhaustVfx : MonoBehaviour
    {
        enum ExhaustStyle
        {
            MainRocket,
            SmallRocket,
            Propeller
        }

        NeoXBehaviorModule module;
        Transform effectRoot;
        ParticleSystem nozzleGlow;
        ParticleSystem core;
        ParticleSystem plume;
        ParticleSystem turbulence;
        ExhaustStyle style;
        float targetThrottle;
        float currentThrottle;
        float presentationScale = 1f;
        float emissionScale = 1f;
        Material exhaustMaterial;

        public static bool Supports(NeoXBehaviorModule value)
        {
            if (value == null)
                return false;
            string id = value.SourceId ?? string.Empty;
            return value.BehaviorKind == GridModuleBehaviorKind.Thruster
                   || id.IndexOf("rocket", StringComparison.OrdinalIgnoreCase) >= 0
                   || id.IndexOf("propeller", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void Configure(NeoXBehaviorModule source)
        {
            module = source;
            string id = source?.SourceId ?? string.Empty;
            if (id.IndexOf(
                    "small_propeller_224",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                style = ExhaustStyle.Propeller;
            }
            else if (id.IndexOf(
                         "speed_rocketsmall_112",
                         StringComparison.OrdinalIgnoreCase) >= 0)
            {
                style = ExhaustStyle.SmallRocket;
            }
            else
            {
                style = ExhaustStyle.MainRocket;
            }
        }

        public void SetTargetThrottle(float value)
        {
            targetThrottle = Mathf.Clamp01(value);
            if (targetThrottle > 0.001f && effectRoot == null)
                BuildEffect();
        }

        public void SetPresentationTuning(float sizeScale, float brightnessScale)
        {
            presentationScale = Mathf.Clamp(sizeScale, 0.1f, 2f);
            emissionScale = Mathf.Clamp(brightnessScale, 0.1f, 2f);
            if (effectRoot != null)
                ApplyThrottle();
        }

        void LateUpdate()
        {
            if (module == null)
                return;
            RobocraftMotionCoordinator rc1 =
                GetComponentInParent<RobocraftMotionCoordinator>();
            bool rc1Active = rc1 != null && rc1.IsActive;
            if (!rc1Active)
                targetThrottle = 0f;

            float response = targetThrottle > currentThrottle ? 12f : 8f;
            currentThrottle = Mathf.MoveTowards(
                currentThrottle,
                targetThrottle,
                response * Time.deltaTime);
            if (effectRoot == null)
                return;

            Vector3 direction = module.WorldExhaustDirection;
            if (direction.sqrMagnitude < 0.001f)
                direction = transform.forward;
            direction.Normalize();
            Vector3 referenceUp =
                Mathf.Abs(Vector3.Dot(direction, transform.up)) > 0.96f
                    ? transform.right
                    : transform.up;
            float nozzleClearance = style == ExhaustStyle.SmallRocket
                ? 0.08f
                : style == ExhaustStyle.Propeller
                    ? 0.1f
                    : 0.14f;
            effectRoot.SetPositionAndRotation(
                module.WorldExhaustPosition +
                direction * nozzleClearance,
                Quaternion.LookRotation(direction, referenceUp));

            ApplyThrottle();
        }

        void OnDisable()
        {
            targetThrottle = 0f;
            currentThrottle = 0f;
            Stop(nozzleGlow);
            Stop(core);
            Stop(plume);
            Stop(turbulence);
        }

        void BuildEffect()
        {
            string sourceName;
            switch (style)
            {
                case ExhaustStyle.SmallRocket:
                    sourceName = "rocketsmall_boost.sfx";
                    break;
                case ExhaustStyle.Propeller:
                    sourceName = "propeller_halo.sfx";
                    break;
                default:
                    sourceName = "rocket_boost.sfx";
                    break;
            }

            effectRoot = new GameObject(
                "NeoXExhaust_" + sourceName).transform;
            effectRoot.SetParent(transform, false);
            exhaustMaterial = CreateMaterial();

            if (style == ExhaustStyle.Propeller)
            {
                nozzleGlow = CreateParticles(
                    "PropellerNozzleGlow",
                    new Color(0.72f, 1.3f, 1.6f, 0.95f),
                    0.035f,
                    0.07f,
                    0.02f,
                    0.08f,
                    0.92f,
                    ParticleSystemShapeType.Circle);
                ConfigureDisc(nozzleGlow, 0.7f, 0.06f);
                core = CreateParticles(
                    "PropellerHalo",
                    new Color(0.2f, 1.05f, 1.4f, 0.92f),
                    0.1f,
                    0.28f,
                    0.02f,
                    0.3f,
                    0.84f,
                    ParticleSystemShapeType.Circle);
                ConfigureDisc(core, 0.76f, 0.09f);
                plume = CreateParticles(
                    "PropellerWake",
                    new Color(0.12f, 0.72f, 1.3f, 0.48f),
                    0.3f,
                    0.68f,
                    2.2f,
                    5.2f,
                    0.48f,
                    ParticleSystemShapeType.Cone);
                turbulence = CreateParticles(
                    "PropellerTurbulence",
                    new Color(0.08f, 0.42f, 0.9f, 0.22f),
                    0.42f,
                    0.95f,
                    1.4f,
                    3.8f,
                    0.82f,
                    ParticleSystemShapeType.Cone);
                ConfigureCone(plume, 8f, 0.44f, 3.2f);
                ConfigureCone(turbulence, 18f, 0.58f, 3.8f);
                EnableNoise(turbulence, 0.32f, 0.42f);
            }
            else
            {
                float scale =
                    style == ExhaustStyle.SmallRocket ? 0.62f : 1f;
                nozzleGlow = CreateParticles(
                    "NozzleGlow",
                    new Color(1.8f, 1.8f, 1.35f, 1f),
                    0.025f,
                    0.065f,
                    0.04f,
                    0.18f,
                    0.7f * scale,
                    ParticleSystemShapeType.Circle);
                ConfigureDisc(
                    nozzleGlow,
                    0.22f * scale,
                    0.65f);
                core = CreateParticles(
                    "HotCore",
                    new Color(1.6f, 1.7f, 1.5f, 1f),
                    0.1f,
                    0.22f,
                    7f,
                    13f,
                    0.3f * scale,
                    ParticleSystemShapeType.Cone);
                plume = CreateParticles(
                    "IonPlume",
                    style == ExhaustStyle.SmallRocket
                        ? new Color(0.05f, 1.15f, 1.65f, 0.82f)
                        : new Color(0.1f, 0.72f, 1.75f, 0.86f),
                    0.24f,
                    style == ExhaustStyle.SmallRocket ? 0.52f : 0.72f,
                    8f,
                    style == ExhaustStyle.SmallRocket ? 17f : 22f,
                    0.62f * scale,
                    ParticleSystemShapeType.Cone);
                turbulence = CreateParticles(
                    "OuterTurbulence",
                    style == ExhaustStyle.SmallRocket
                        ? new Color(0.04f, 0.55f, 1.4f, 0.3f)
                        : new Color(0.08f, 0.3f, 1.25f, 0.34f),
                    0.38f,
                    style == ExhaustStyle.SmallRocket ? 0.78f : 1.05f,
                    6f,
                    style == ExhaustStyle.SmallRocket ? 13f : 16f,
                    0.92f * scale,
                    ParticleSystemShapeType.Cone);
                ConfigureCone(core, 3f, 0.075f * scale, 3.5f);
                ConfigureCone(
                    plume,
                    style == ExhaustStyle.SmallRocket ? 7f : 11f,
                    0.14f * scale,
                    4.2f);
                ConfigureCone(
                    turbulence,
                    style == ExhaustStyle.SmallRocket ? 13f : 18f,
                    0.2f * scale,
                    4.8f);
                EnableNoise(turbulence, 0.42f, 0.58f);
                EnableTrails(core);
                EnableTrails(plume);
            }

            ApplyThrottle();
        }

        ParticleSystem CreateParticles(
            string objectName,
            Color color,
            float minimumLifetime,
            float maximumLifetime,
            float minimumSpeed,
            float maximumSpeed,
            float size,
            ParticleSystemShapeType shapeType)
        {
            GameObject value = new GameObject(
                objectName,
                typeof(ParticleSystem));
            value.transform.SetParent(effectRoot, false);
            ParticleSystem particles = value.GetComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 420;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                minimumLifetime,
                maximumLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                minimumSpeed,
                maximumSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(
                size * 0.65f,
                size);
            main.startColor = color;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = shapeType;
            shape.angle = 8f;
            shape.radius = Mathf.Max(0.02f, size * 0.25f);

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(color, 0.22f),
                    new GradientColorKey(
                        new Color(
                            color.r * 0.35f,
                            color.g * 0.55f,
                            color.b,
                            1f),
                        1f)
                },
                new[]
                {
                    new GradientAlphaKey(color.a, 0f),
                    new GradientAlphaKey(color.a * 0.9f, 0.45f),
                    new GradientAlphaKey(0f, 1f)
                });
            ParticleSystem.ColorOverLifetimeModule lifetimeColor =
                particles.colorOverLifetime;
            lifetimeColor.enabled = true;
            lifetimeColor.color =
                new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule lifetimeSize =
                particles.sizeOverLifetime;
            lifetimeSize.enabled = true;
            lifetimeSize.size = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1f));

            ParticleSystemRenderer renderer =
                value.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 3.1f;
            renderer.velocityScale = 0.12f;
            renderer.material = exhaustMaterial;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return particles;
        }

        static void ConfigureDisc(
            ParticleSystem particles,
            float radius,
            float thickness)
        {
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = Mathf.Clamp01(thickness);
            ParticleSystemRenderer renderer =
                particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = 0f;
            renderer.velocityScale = 0f;
        }

        static void ConfigureCone(
            ParticleSystem particles,
            float angle,
            float radius,
            float lengthScale)
        {
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
            ParticleSystemRenderer renderer =
                particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = lengthScale;
            renderer.velocityScale = 0.16f;
        }

        static void EnableNoise(
            ParticleSystem particles,
            float strength,
            float frequency)
        {
            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.separateAxes = false;
            noise.strength = strength;
            noise.frequency = frequency;
            noise.scrollSpeed = 0.7f;
            noise.quality = ParticleSystemNoiseQuality.Medium;
        }

        void EnableTrails(ParticleSystem particles)
        {
            ParticleSystem.TrailModule trails = particles.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 0.88f;
            trails.lifetime = 0.34f;
            trails.dieWithParticles = true;
            trails.sizeAffectsWidth = true;
            ParticleSystemRenderer renderer =
                particles.GetComponent<ParticleSystemRenderer>();
            renderer.trailMaterial = exhaustMaterial;
        }

        void ApplyThrottle()
        {
            if (nozzleGlow == null || core == null || plume == null
                || turbulence == null)
                return;
            bool active = currentThrottle > 0.002f;
            float visibleThrottle = Mathf.Pow(currentThrottle, 0.42f);
            SetEmission(
                nozzleGlow,
                style == ExhaustStyle.Propeller
                    ? 145f * visibleThrottle * emissionScale
                    : 210f * visibleThrottle * emissionScale);
            SetEmission(
                core,
                style == ExhaustStyle.Propeller
                    ? 125f * visibleThrottle * emissionScale
                    : 190f * visibleThrottle * emissionScale);
            SetEmission(
                plume,
                style == ExhaustStyle.Propeller
                    ? 74f * visibleThrottle * emissionScale
                    : 135f * visibleThrottle * emissionScale);
            SetEmission(
                turbulence,
                style == ExhaustStyle.Propeller
                    ? 42f * visibleThrottle * emissionScale
                    : 68f * visibleThrottle * emissionScale);
            SetPlaying(nozzleGlow, active);
            SetPlaying(core, active);
            SetPlaying(plume, active);
            SetPlaying(turbulence, active);

            float width = Mathf.Lerp(0.52f, 1.18f, visibleThrottle);
            float length = Mathf.Lerp(0.6f, 2.35f, visibleThrottle);
            effectRoot.localScale = new Vector3(width, width, length) *
                                    presentationScale;
        }

        static void SetEmission(
            ParticleSystem particles,
            float rate)
        {
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = rate;
        }

        static void SetPlaying(
            ParticleSystem particles,
            bool active)
        {
            if (active)
            {
                if (!particles.isEmitting)
                    particles.Play(true);
            }
            else if (particles.isEmitting)
            {
                particles.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmitting);
            }
        }

        static void Stop(ParticleSystem particles)
        {
            if (particles != null)
            {
                particles.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        static Material CreateMaterial()
        {
            Shader shader =
                Shader.Find("UnityPlanet/NeoXThrusterExhaust")
                ?? Shader.Find(
                    "Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Sprites/Default");
            Material material = new Material(shader)
            {
                name = "NeoXThrusterExhaust_Runtime",
                hideFlags = HideFlags.HideAndDontSave
            };
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            return material;
        }
    }
}
