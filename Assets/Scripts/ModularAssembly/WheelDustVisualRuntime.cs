using System;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityPlanet.ModularAssembly
{
    /// <summary>
    /// Visual-only tyre dust. This component reads wheel contact telemetry but
    /// never writes to the wheel, a Rigidbody, or any Collider.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WheelDustVisualRuntime : MonoBehaviour
    {
        public const string DustMaterialResourcePath =
            "CombatFeedback/NeoX/Materials/NeoX_metal_hit_00";
        public const float MinimumEmissionSpeed = 1.2f;

        private const float Gravity = 9.81f;
        private const float MinimumSupportedRadius = 0.12f;
        // The source dirt SFX uses 30 particles/second. Treat that as the
        // module-wide full-slip ceiling; dual tyres share the same budget.
        private const float MaximumModuleEmissionRate = 30f;
        private const int MaximumEmissionsPerFrame = 12;
        private static readonly string[] NonDirtSurfaceTokens =
        {
            "water",
            "ocean",
            "river",
            "lake",
            "ice",
            "frozen",
            "snow",
            "glass",
            "metal",
            "steel",
            "road",
            "asphalt",
            "concrete"
        };

        private ModularWheelRuntime wheel;
        private Transform effectRoot;
        private ParticleSystem particles;
        private Material dustMaterial;
        private float currentEmissionRate;
        private float emissionAccumulator;
        private Vector3 lastEmissionPosition;
        private Vector3 lastEmissionNormal = Vector3.up;
        private uint randomState;
        private Collider cachedSurfaceCollider;
        private bool cachedSurfaceAllowsDust;

        public ModularWheelRuntime BoundWheel => wheel;
        public Transform EffectRoot => effectRoot;
        public ParticleSystem Particles => particles;
        public Material DustMaterial => dustMaterial;
        public float CurrentEmissionRate => currentEmissionRate;
        public Vector3 LastEmissionPosition => lastEmissionPosition;
        public Vector3 LastEmissionNormal => lastEmissionNormal;

        /// <summary>
        /// Binding is explicit so adding this presentation component to a
        /// carrier cannot accidentally turn the carrier into a dust emitter.
        /// Bind only after the ModularWheelRuntime has its authored geometry.
        /// </summary>
        public void Bind(ModularWheelRuntime source)
        {
            wheel = source;
            currentEmissionRate = 0f;
            emissionAccumulator = 0f;
            cachedSurfaceCollider = null;
            cachedSurfaceAllowsDust = false;

            if (wheel == null)
            {
                if (particles != null)
                {
                    particles.Stop(
                        false,
                        ParticleSystemStopBehavior.StopEmitting);
                }
                return;
            }

            if (effectRoot == null)
                BuildEffect();
            else
            {
                effectRoot.gameObject.layer = gameObject.layer;
                if (Application.isPlaying &&
                    particles != null)
                    particles.Play(false);
            }
        }

        public static float ComputeActivitySpeed(
            WheelContactTelemetry telemetry,
            float tyreRadius)
        {
            float planarContactSpeed = Mathf.Sqrt(
                telemetry.longitudinalSpeed *
                telemetry.longitudinalSpeed +
                telemetry.lateralSpeed *
                telemetry.lateralSpeed);
            float treadSpeed =
                Mathf.Abs(telemetry.wheelAngularSpeed) *
                Mathf.Max(MinimumSupportedRadius, tyreRadius);
            return Mathf.Max(planarContactSpeed, treadSpeed);
        }

        public static bool ShouldEmit(
            bool wheelIsGrounded,
            WheelContactTelemetry telemetry,
            float tyreRadius)
        {
            if (!wheelIsGrounded || !telemetry.grounded)
                return false;

            float activitySpeed =
                ComputeActivitySpeed(telemetry, tyreRadius);
            if (activitySpeed < MinimumEmissionSpeed)
                return false;

            float supportedWeight = Mathf.Max(
                25f,
                Mathf.Max(0f, telemetry.sprungMass) *
                Gravity * 0.01f);
            return telemetry.normalForce >= supportedWeight;
        }

        public static float ComputeSlipSeverity(
            WheelContactTelemetry telemetry,
            float tyreRadius)
        {
            float ratioSeverity = Mathf.InverseLerp(
                0.045f,
                0.55f,
                Mathf.Abs(telemetry.slipRatio));
            float angleSeverity = Mathf.InverseLerp(
                3f,
                28f,
                Mathf.Abs(telemetry.slipAngleDegrees));
            float lateralSeverity = Mathf.InverseLerp(
                0.4f,
                7f,
                Mathf.Abs(telemetry.lateralSpeed));
            float surfaceMismatch = Mathf.Abs(
                telemetry.wheelAngularSpeed *
                Mathf.Max(MinimumSupportedRadius, tyreRadius) -
                telemetry.longitudinalSpeed);
            float mismatchSeverity = Mathf.InverseLerp(
                0.65f,
                7f,
                surfaceMismatch);
            return Mathf.Clamp01(
                Mathf.Max(
                    ratioSeverity,
                    angleSeverity,
                    lateralSeverity,
                    mismatchSeverity));
        }

        /// <summary>
        /// Returns particles per second for one tyre element. The module-wide
        /// amount is divided by element count so a dual-tyre module does not
        /// receive twice the dust simply because it owns two contact solvers.
        /// </summary>
        public static float ComputeEmissionRate(
            bool wheelIsGrounded,
            WheelContactTelemetry telemetry,
            float tyreRadius,
            int tyreElementCount)
        {
            if (!ShouldEmit(
                    wheelIsGrounded,
                    telemetry,
                    tyreRadius))
            {
                return 0f;
            }

            float activitySpeed =
                ComputeActivitySpeed(telemetry, tyreRadius);
            float speedAmount = Mathf.InverseLerp(
                MinimumEmissionSpeed,
                20f,
                activitySpeed);
            float slipSeverity =
                ComputeSlipSeverity(telemetry, tyreRadius);

            float referenceLoad = Mathf.Max(
                250f,
                Mathf.Max(0f, telemetry.sprungMass) * Gravity);
            float loadRatio =
                Mathf.Max(0f, telemetry.normalForce) /
                referenceLoad;
            float loadAmount = Mathf.InverseLerp(
                0.04f,
                1.5f,
                loadRatio);
            float loadScale = Mathf.Lerp(
                0.35f,
                1.3f,
                loadAmount);

            // A freely rolling tyre leaves only a sparse trace. Longitudinal
            // or lateral slip adds a much denser but bounded cloud.
            float rollingRate = Mathf.Lerp(
                1.8f,
                8.5f,
                speedAmount);
            float slipRate =
                Mathf.Lerp(12f, 34f, speedAmount) *
                Mathf.Pow(slipSeverity, 1.25f);
            float moduleRate = Mathf.Min(
                MaximumModuleEmissionRate,
                (rollingRate + slipRate) * loadScale);
            return moduleRate / Mathf.Max(1, tyreElementCount);
        }

        public static float SmoothEmissionRate(
            float currentRate,
            float targetRate,
            float deltaTime)
        {
            float target = Mathf.Max(0f, targetRate);
            if (target <= 0f || deltaTime <= 0f)
                return target <= 0f ? 0f : Mathf.Max(0f, currentRate);

            float current = Mathf.Max(0f, currentRate);
            float response = target > current ? 7f : 3.5f;
            float blend =
                1f - Mathf.Exp(-response * deltaTime);
            return Mathf.Lerp(current, target, blend);
        }

        /// <summary>
        /// Presentation-only semantic gate. Unknown driveable surfaces remain
        /// compatible, while obvious wet or hard manufactured surfaces do not
        /// receive the brown NeoX dirt effect.
        /// </summary>
        public static bool SupportsDirtVisual(Collider surface)
        {
            if (surface == null)
                return false;

            if (HasNonDirtSurfaceToken(
                    LayerMask.LayerToName(surface.gameObject.layer)) ||
                HasNonDirtSurfaceToken(surface.name) ||
                HasNonDirtSurfaceToken(
                    surface.sharedMaterial != null
                        ? surface.sharedMaterial.name
                        : string.Empty))
            {
                return false;
            }

            Transform ancestor = surface.transform.parent;
            for (int depth = 0;
                 ancestor != null && depth < 4;
                 depth++, ancestor = ancestor.parent)
            {
                if (HasNonDirtSurfaceToken(ancestor.name))
                    return false;
            }

            Renderer renderer =
                surface.GetComponent<Renderer>() ??
                surface.GetComponentInParent<Renderer>();
            if (renderer != null)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0;
                     index < materials.Length;
                     index++)
                {
                    if (materials[index] != null &&
                        HasNonDirtSurfaceToken(
                            materials[index].name))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public static float ComputeInitialParticleSize(
            float tyreRadius,
            float tyreWidth,
            float slipSeverity)
        {
            float radius = Mathf.Max(
                MinimumSupportedRadius,
                Mathf.Abs(tyreRadius));
            float width = Mathf.Max(0.08f, Mathf.Abs(tyreWidth));
            float maximum = width * 0.48f;
            float baseSize = Mathf.Clamp(
                radius * 0.12f + width * 0.09f,
                Mathf.Min(0.035f, maximum),
                maximum);
            return Mathf.Lerp(
                baseSize,
                Mathf.Min(maximum, baseSize * 1.55f),
                Mathf.Clamp01(slipSeverity));
        }

        public static float ComputeParticleLifetime(
            float tyreRadius,
            float normalizedVariation)
        {
            // NeoX's small dirt particles live for roughly 0.75-1.0 seconds
            // and its larger variants reach roughly 1.2 seconds. Its source
            // length/velocity units are not assumed to be Unity metres.
            float radiusAmount = Mathf.InverseLerp(
                0.35f,
                1.35f,
                Mathf.Abs(tyreRadius));
            float minimum = Mathf.Lerp(
                0.75f,
                0.92f,
                radiusAmount);
            float maximum = Mathf.Lerp(
                1f,
                1.2f,
                radiusAmount);
            return Mathf.Lerp(
                minimum,
                maximum,
                Mathf.Clamp01(normalizedVariation));
        }

        public static Vector3 ComputeEmissionOrigin(
            Vector3 contactPoint,
            Vector3 contactNormal,
            Vector3 movementDirection,
            float tyreRadius)
        {
            Vector3 normal = contactNormal.sqrMagnitude > 0.0001f
                ? contactNormal.normalized
                : Vector3.up;
            Vector3 direction = Vector3.ProjectOnPlane(
                movementDirection,
                normal);
            if (direction.sqrMagnitude > 0.0001f)
                direction.Normalize();

            float radius = Mathf.Max(
                MinimumSupportedRadius,
                Mathf.Abs(tyreRadius));
            return contactPoint -
                   direction * (radius * 0.09f) +
                   normal * Mathf.Clamp(
                       radius * 0.018f,
                       0.012f,
                       0.04f);
        }

        private void LateUpdate()
        {
            if (wheel == null ||
                particles == null ||
                dustMaterial == null)
            {
                currentEmissionRate = 0f;
                emissionAccumulator = 0f;
                return;
            }

            WheelContactTelemetry telemetry = wheel.Telemetry;
            float targetRate = ComputeEmissionRate(
                wheel.IsGrounded,
                telemetry,
                wheel.Profile.radius,
                wheel.TyreElementCount);
            if (targetRate > 0f &&
                !SurfaceAllowsDust(wheel.ContactCollider))
            {
                targetRate = 0f;
            }
            if (targetRate <= 0f)
            {
                // Airborne, flight-mode, stationary and unloaded wheels stop
                // creating particles immediately; existing world-space dust
                // is left to expand and fade naturally.
                currentEmissionRate = 0f;
                emissionAccumulator = 0f;
                return;
            }

            float deltaTime = Mathf.Max(0f, Time.deltaTime);
            currentEmissionRate = SmoothEmissionRate(
                currentEmissionRate,
                targetRate,
                deltaTime);

            Vector3 normal = wheel.ContactNormal;
            if (normal.sqrMagnitude < 0.0001f)
                normal = Vector3.up;
            normal.Normalize();
            Vector3 movementDirection = ResolveMovementDirection(
                telemetry,
                normal);
            Vector3 origin = ComputeEmissionOrigin(
                wheel.ContactPoint,
                normal,
                movementDirection,
                wheel.Profile.radius);
            effectRoot.SetPositionAndRotation(
                origin,
                ResolveEmitterRotation(movementDirection, normal));

            emissionAccumulator +=
                currentEmissionRate * deltaTime;
            int requestedCount =
                Mathf.FloorToInt(emissionAccumulator);
            if (requestedCount <= 0)
                return;

            // Drop excess particles after a frame hitch instead of emitting a
            // delayed wall of dust over subsequent frames.
            emissionAccumulator -= requestedCount;
            int emissionCount = Mathf.Min(
                requestedCount,
                MaximumEmissionsPerFrame);
            float severity = ComputeSlipSeverity(
                telemetry,
                wheel.Profile.radius);
            float activitySpeed =
                ComputeActivitySpeed(
                    telemetry,
                    wheel.Profile.radius);
            for (int index = 0; index < emissionCount; index++)
            {
                EmitParticle(
                    origin,
                    normal,
                    movementDirection,
                    severity,
                    activitySpeed);
            }
        }

        private void OnDisable()
        {
            currentEmissionRate = 0f;
            emissionAccumulator = 0f;
        }

        private void BuildEffect()
        {
            GameObject effect = new GameObject(
                "WheelDustVisual",
                typeof(ParticleSystem));
            effect.layer = gameObject.layer;
            effectRoot = effect.transform;
            effectRoot.SetParent(transform, false);
            particles = effect.GetComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace =
                ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = 256;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                0.75f,
                1.2f);
            main.startSpeed = 0f;
            main.startSize = 0.1f;
            main.startColor = Color.white;
            // Physics.gravity is a world-space vector, while this project can
            // drive on every side of a spherical planet. A gravity modifier
            // would therefore bend dust toward world -Y on the planet's
            // sides/back. Particles instead leave along the sampled contact
            // normal and fade before a ballistic fall is visually expected.
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission =
                particles.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = false;
            ParticleSystem.CollisionModule collision =
                particles.collision;
            collision.enabled = false;
            ParticleSystem.TriggerModule trigger =
                particles.trigger;
            trigger.enabled = false;
            ParticleSystem.ExternalForcesModule externalForces =
                particles.externalForces;
            externalForces.enabled = false;

            Gradient color = new Gradient();
            color.SetKeys(
                new[]
                {
                    new GradientColorKey(
                        new Color(0.48f, 0.39f, 0.29f),
                        0f),
                    new GradientColorKey(
                        new Color(0.38f, 0.34f, 0.29f),
                        0.55f),
                    new GradientColorKey(
                        new Color(0.28f, 0.27f, 0.25f),
                        1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.52f, 0f),
                    new GradientAlphaKey(0.3f, 0.48f),
                    new GradientAlphaKey(0f, 1f)
                });
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime =
                particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color =
                new ParticleSystem.MinMaxGradient(color);

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime =
                particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.72f),
                    new Keyframe(0.38f, 1.22f),
                    new Keyframe(1f, 1.85f)));

            ParticleSystemRenderer renderer =
                effect.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode =
                ParticleSystemRenderMode.Billboard;
            renderer.alignment =
                ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage =
                LightProbeUsage.Off;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;

            // Use the imported NeoX dirt/smoke material directly. Assigning it
            // as a shared material does not instantiate or mutate the asset.
            dustMaterial = Resources.Load<Material>(
                DustMaterialResourcePath);
            renderer.sharedMaterial = dustMaterial;
            renderer.enabled = dustMaterial != null;

            if (Application.isPlaying)
                particles.Play(false);
        }

        private Vector3 ResolveMovementDirection(
            WheelContactTelemetry telemetry,
            Vector3 normal)
        {
            Vector3 forward = Vector3.ProjectOnPlane(
                wheel.BaseForwardWorld,
                normal);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(
                    transform.forward,
                    normal);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 right =
                Vector3.Cross(normal, forward);
            if (right.sqrMagnitude > 0.0001f)
                right.Normalize();
            Vector3 movement =
                forward * telemetry.longitudinalSpeed +
                right * telemetry.lateralSpeed;
            if (movement.sqrMagnitude < 0.0001f)
            {
                float rollingDirection =
                    Mathf.Sign(telemetry.wheelAngularSpeed);
                movement = forward *
                           (Mathf.Abs(rollingDirection) > 0f
                               ? rollingDirection
                               : 1f);
            }
            return movement.normalized;
        }

        private static Quaternion ResolveEmitterRotation(
            Vector3 movementDirection,
            Vector3 normal)
        {
            Vector3 tangent = Vector3.ProjectOnPlane(
                movementDirection,
                normal);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.ProjectOnPlane(
                    Vector3.forward,
                    normal);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.right;
            return Quaternion.LookRotation(
                tangent.normalized,
                normal);
        }

        private bool SurfaceAllowsDust(Collider surface)
        {
            if (surface != cachedSurfaceCollider)
            {
                cachedSurfaceCollider = surface;
                cachedSurfaceAllowsDust =
                    SupportsDirtVisual(surface);
            }
            return cachedSurfaceAllowsDust;
        }

        private static bool HasNonDirtSurfaceToken(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            string semanticName =
                NormalizeSemanticName(value);
            for (int index = 0;
                 index < NonDirtSurfaceTokens.Length;
                 index++)
            {
                if (semanticName.IndexOf(
                        " " + NonDirtSurfaceTokens[index] + " ",
                        StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static string NormalizeSemanticName(string value)
        {
            var result = new StringBuilder(value.Length + 2);
            result.Append(' ');
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (!char.IsLetterOrDigit(current))
                {
                    if (result[result.Length - 1] != ' ')
                        result.Append(' ');
                    continue;
                }

                bool camelBoundary =
                    index > 0 &&
                    char.IsUpper(current) &&
                    (char.IsLower(value[index - 1]) ||
                     (index + 1 < value.Length &&
                      char.IsLower(value[index + 1])));
                if (camelBoundary &&
                    result[result.Length - 1] != ' ')
                {
                    result.Append(' ');
                }
                result.Append(char.ToLowerInvariant(current));
            }
            if (result[result.Length - 1] != ' ')
                result.Append(' ');
            return result.ToString();
        }

        private void EmitParticle(
            Vector3 origin,
            Vector3 normal,
            Vector3 movementDirection,
            float slipSeverity,
            float activitySpeed)
        {
            Vector3 lateral =
                Vector3.Cross(normal, movementDirection);
            if (lateral.sqrMagnitude > 0.0001f)
                lateral.Normalize();
            else
                lateral = wheel.BaseAxleWorld.normalized;

            float width = Mathf.Max(
                0.08f,
                wheel.Profile.width);
            float size = ComputeInitialParticleSize(
                wheel.Profile.radius,
                width,
                slipSeverity);
            Vector3 position =
                origin +
                lateral * NextRandomRange(
                    -width * 0.28f,
                    width * 0.28f);
            Vector3 velocity =
                -movementDirection *
                NextRandomRange(0.012f, 0.035f) *
                Mathf.Min(activitySpeed, 18f) +
                normal *
                NextRandomRange(0.65f, 1.25f) *
                Mathf.Lerp(0.85f, 1.25f, slipSeverity) +
                lateral * NextRandomRange(-0.22f, 0.22f);
            float variedSize = Mathf.Min(
                width * 0.48f,
                size * NextRandomRange(0.82f, 1f));

            ParticleSystem.EmitParams values =
                new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = velocity,
                    startLifetime = ComputeParticleLifetime(
                        wheel.Profile.radius,
                        NextRandom01()),
                    startSize = variedSize,
                    startColor = Color.Lerp(
                        new Color(0.54f, 0.48f, 0.4f, 0.32f),
                        new Color(0.45f, 0.34f, 0.23f, 0.56f),
                        slipSeverity),
                    rotation = NextRandomRange(0f, 360f),
                    randomSeed = NextRandomUInt()
                };
            particles.Emit(values, 1);
            lastEmissionPosition = position;
            lastEmissionNormal = normal;
        }

        private uint NextRandomUInt()
        {
            if (randomState == 0u)
            {
                randomState = unchecked(
                    (uint)GetInstanceID() * 747796405u +
                    2891336453u);
                if (randomState == 0u)
                    randomState = 1u;
            }

            randomState = unchecked(
                randomState * 1664525u + 1013904223u);
            return randomState;
        }

        private float NextRandom01()
        {
            return (NextRandomUInt() >> 8) *
                   (1f / 16777216f);
        }

        private float NextRandomRange(
            float minimum,
            float maximum)
        {
            return Mathf.Lerp(
                minimum,
                maximum,
                NextRandom01());
        }
    }
}
