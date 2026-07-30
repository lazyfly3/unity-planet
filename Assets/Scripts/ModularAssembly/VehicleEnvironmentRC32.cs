using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    [Serializable]
    public struct PlanetEnvironmentSample
    {
        public Vector3 gravityAcceleration;
        public float altitude;
        public float airDensity;
        public float ambientPressure;
        public Vector3 meanWindVelocity;
        public Vector3 gustVelocity;
        public Vector3 atmosphereVelocity;
        public Vector3 surfacePoint;
        public Vector3 surfaceNormal;
        public float surfaceDistance;
        public bool hasSurface;
        public bool hasAtmosphere;

        public static PlanetEnvironmentSample EarthLike(Vector3 gravity, float altitude)
        {
            float density = 1.225f * Mathf.Exp(-Mathf.Max(0f, altitude) / 8500f);
            return new PlanetEnvironmentSample
            {
                gravityAcceleration = gravity,
                altitude = Mathf.Max(0f, altitude),
                airDensity = density,
                ambientPressure = 101325f * density / 1.225f,
                surfacePoint = Vector3.up * -Mathf.Max(0f, altitude),
                surfaceNormal = Vector3.up,
                surfaceDistance = Mathf.Max(0f, altitude),
                hasSurface = true,
                hasAtmosphere = density > 0.000001f
            };
        }
    }

    public interface IPlanetEnvironmentProvider
    {
        PlanetEnvironmentSample Sample(Vector3 worldPosition, double simulationTime);
        bool ForceNoWind { get; set; }
    }

    public static class PlanetEnvironmentRuntime
    {
        public static IPlanetEnvironmentProvider Active { get; internal set; }

        public static PlanetEnvironmentSample Sample(
            Vector3 worldPosition,
            double simulationTime)
        {
            return Active != null
                ? Active.Sample(worldPosition, simulationTime)
                : PlanetEnvironmentSample.EarthLike(
                    Vector3.down * 9.81f,
                    Mathf.Max(0f, worldPosition.y));
        }
    }

    [DisallowMultipleComponent]
    public sealed class PlanetEnvironmentProvider :
        MonoBehaviour,
        IPlanetEnvironmentProvider
    {
        InfinitePlanarSurfaceWorld world;
        PlanetPhysicalProfile physical;
        PlanetWindProfile wind;

        public bool ForceNoWind { get; set; }

        public void Configure(
            InfinitePlanarSurfaceWorld surfaceWorld,
            PlanetPhysicalProfile physicalProfile)
        {
            world = surfaceWorld;
            physical = physicalProfile != null
                ? physicalProfile.Clone()
                : PlanetPhysicalProfile.CreateEarthLike();
            wind = physical.wind != null
                ? physical.wind.Clone()
                : new PlanetWindProfile();
            wind.ClampValues(physical.HasAtmosphere);
            PlanetEnvironmentRuntime.Active = this;
        }

        public PlanetEnvironmentSample Sample(
            Vector3 worldPosition,
            double simulationTime)
        {
            float groundHeight = 0f;
            Vector3 groundNormal = Vector3.up;
            bool hasSurface = false;
            if (world != null && world.Streamer != null)
            {
                hasSurface = world.Streamer.TrySampleSurface(
                    worldPosition.x,
                    worldPosition.z,
                    out groundHeight,
                    out groundNormal);
                if (!hasSurface)
                {
                    groundHeight = world.Streamer.SampleHeight(
                        worldPosition.x,
                        worldPosition.z);
                    groundNormal = Vector3.up;
                    hasSurface = true;
                }
            }

            float altitude = Mathf.Max(0f, worldPosition.y - groundHeight);
            Vector3 gravity = world != null
                ? world.GetGravity(worldPosition)
                : Vector3.down * (float)Math.Max(0d, physical.surfaceGravity);
            float density = (float)physical.AtmosphereDensityAtAltitude(altitude);
            bool hasAtmosphere = physical.HasAtmosphere && density > 0.000001f;
            float surfaceDensity = (float)Math.Max(
                0.000001d,
                physical.atmosphereSurfaceDensityKgPerCubicMeter);
            float surfacePressure = surfaceDensity *
                                    (float)Math.Max(0.01d, physical.surfaceGravity) *
                                    (float)Math.Max(
                                        1d,
                                        physical.atmosphereScaleHeightMeters);
            float pressure = hasAtmosphere
                ? surfacePressure * density / surfaceDensity
                : 0f;

            Vector3 mean = Vector3.zero;
            Vector3 gust = Vector3.zero;
            if (hasAtmosphere && !ForceNoWind && wind != null && wind.enabled)
            {
                Vector3 direction = Vector3.ProjectOnPlane(
                    wind.referenceDirection,
                    Vector3.up);
                if (direction.sqrMagnitude < 0.0001f)
                    direction = Vector3.forward;
                direction.Normalize();
                float shearHeight = Mathf.Max(0.1f, altitude + 0.1f);
                float shear = Mathf.Pow(
                    shearHeight / 10f,
                    wind.shearExponent);
                shear = Mathf.Clamp(shear, 0.2f, 4f);
                mean = direction * wind.referenceWindSpeed * shear;
                gust = SampleGust(
                    worldPosition,
                    simulationTime,
                    direction,
                    wind);
            }

            return new PlanetEnvironmentSample
            {
                gravityAcceleration = gravity,
                altitude = altitude,
                airDensity = density,
                ambientPressure = pressure,
                meanWindVelocity = mean,
                gustVelocity = gust,
                atmosphereVelocity = mean + gust,
                surfacePoint = new Vector3(worldPosition.x, groundHeight, worldPosition.z),
                surfaceNormal = groundNormal.sqrMagnitude > 0.001f ? groundNormal.normalized : Vector3.up,
                surfaceDistance = worldPosition.y - groundHeight,
                hasSurface = hasSurface,
                hasAtmosphere = hasAtmosphere
            };
        }

        static Vector3 SampleGust(
            Vector3 position,
            double simulationTime,
            Vector3 meanDirection,
            PlanetWindProfile profile)
        {
            float length = Mathf.Max(1f, profile.coherenceLength);
            float x = position.x / length;
            float z = position.z / length;
            float seed = profile.seed * 0.000137f;
            float minimum = Mathf.Max(0.001f, profile.gustFrequencyRange.x);
            float maximum = Mathf.Max(minimum, profile.gustFrequencyRange.y);
            float f0 = Mathf.Lerp(minimum, maximum, Hash01(profile.seed, 11));
            float f1 = Mathf.Lerp(minimum, maximum, Hash01(profile.seed, 29));
            float f2 = Mathf.Lerp(minimum, maximum, Hash01(profile.seed, 47));
            float time = (float)(simulationTime % 100000d);
            float spatial0 = Mathf.PerlinNoise(x + seed + 17.1f, z + 3.7f) * 2f - 1f;
            float spatial1 = Mathf.PerlinNoise(x + seed + 43.3f, z + 31.9f) * 2f - 1f;
            float along = Mathf.Sin(
                (time * f0 + spatial0 + Hash01(profile.seed, 5)) *
                Mathf.PI * 2f);
            float lateral = Mathf.Sin(
                (time * f1 + spatial1 + Hash01(profile.seed, 13)) *
                Mathf.PI * 2f);
            float vertical = Mathf.Sin(
                (time * f2 + spatial0 * 0.6f + Hash01(profile.seed, 23)) *
                Mathf.PI * 2f);
            Vector3 side = Vector3.Cross(Vector3.up, meanDirection).normalized;
            Vector3 shaped =
                meanDirection * along * 0.65f +
                side * lateral * 0.55f +
                Vector3.up * vertical * profile.verticalGustRatio;
            return Vector3.ClampMagnitude(
                shaped * profile.gustPeakSpeed,
                profile.gustPeakSpeed);
        }

        static float Hash01(int seed, int salt)
        {
            unchecked
            {
                uint value = (uint)(seed ^ (salt * 0x45d9f3b));
                value ^= value >> 16;
                value *= 0x7feb352d;
                value ^= value >> 15;
                value *= 0x846ca68b;
                value ^= value >> 16;
                return (value & 0x00ffffff) / 16777215f;
            }
        }

        void OnEnable()
        {
            if (physical != null)
                PlanetEnvironmentRuntime.Active = this;
        }

        void OnDisable()
        {
            if (ReferenceEquals(PlanetEnvironmentRuntime.Active, this))
                PlanetEnvironmentRuntime.Active = null;
        }
    }

    public struct PropellerWashSource
    {
        public string runtimeId;
        public Vector3 worldDiskCenter;
        public Vector3 worldAirflowDirection;
        public float diskRadius;
        public float inducedVelocity;
        public float swirlRatio;
        public float washLength;
        public float occlusionEfficiency;
        public int rotationSign;
    }

    public delegate bool AirflowOcclusionResolver(
        Vector3 worldStart,
        Vector3 worldEnd,
        string sourceRuntimeId,
        string targetRuntimeId);

    public sealed class VehicleAirflowField
    {
        IPlanetEnvironmentProvider provider;
        PlanetEnvironmentSample fallback;
        IReadOnlyList<PropellerWashSource> washes =
            Array.Empty<PropellerWashSource>();
        AirflowOcclusionResolver occlusionResolver;

        public void Configure(
            IPlanetEnvironmentProvider environmentProvider,
            PlanetEnvironmentSample fallbackSample,
            IReadOnlyList<PropellerWashSource> propellerWashes,
            AirflowOcclusionResolver resolver)
        {
            provider = environmentProvider;
            fallback = fallbackSample;
            washes = propellerWashes ?? Array.Empty<PropellerWashSource>();
            occlusionResolver = resolver;
        }

        public PlanetEnvironmentSample Sample(Vector3 worldPoint)
        {
            return provider != null
                ? provider.Sample(worldPoint, Time.fixedTimeAsDouble)
                : fallback;
        }

        public Vector3 RelativeAirVelocity(
            Rigidbody body,
            Vector3 worldPoint,
            string targetRuntimeId)
        {
            if (body == null)
                return Vector3.zero;
            PlanetEnvironmentSample sample = Sample(worldPoint);
            Vector3 washVelocity = Vector3.zero;
            for (int index = 0; index < washes.Count; index++)
            {
                PropellerWashSource wash = washes[index];
                Vector3 axis = wash.worldAirflowDirection.sqrMagnitude > 0.0001f
                    ? wash.worldAirflowDirection.normalized
                    : Vector3.back;
                Vector3 offset = worldPoint - wash.worldDiskCenter;
                float distance = Vector3.Dot(offset, axis);
                if (distance <= 0f || distance > wash.washLength)
                    continue;
                Vector3 radial = offset - axis * distance;
                float radius = wash.diskRadius *
                               (1f + 0.18f * distance /
                                   Mathf.Max(0.1f, wash.diskRadius));
                if (radial.sqrMagnitude > radius * radius)
                    continue;
                if (occlusionResolver != null &&
                    occlusionResolver(
                        wash.worldDiskCenter,
                        worldPoint,
                        wash.runtimeId,
                        targetRuntimeId))
                {
                    continue;
                }

                float areaDecay = wash.diskRadius * wash.diskRadius /
                                  Mathf.Max(
                                      0.01f,
                                      radius * radius);
                float edge = 1f - Mathf.Clamp01(
                    radial.magnitude / Mathf.Max(0.01f, radius));
                float strength = wash.inducedVelocity *
                                 areaDecay *
                                 Mathf.SmoothStep(0f, 1f, edge) *
                                 Mathf.Clamp01(wash.occlusionEfficiency);
                washVelocity += axis * strength;
                if (radial.sqrMagnitude > 0.0001f)
                {
                    Vector3 swirl = Vector3.Cross(
                        axis,
                        radial.normalized) * wash.rotationSign;
                    washVelocity += swirl * strength * wash.swirlRatio;
                }
            }
            return body.GetPointVelocity(worldPoint) -
                   sample.atmosphereVelocity -
                   washVelocity;
        }
    }

    public struct PhysicsConflictAuditResult
    {
        public bool fatal;
        public int disabledLegacyControllers;
        public string message;
    }

    public static class PhysicsConflictAudit
    {
        static readonly HashSet<string> LegacyForceOwners =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "SpacecraftIfcsMotor",
                "ShipFlightController",
                "PlanetSurfaceFlightEnvironment",
                "ModularWheelRuntime",
                "LabEnvironmentBody",
                "LabArcadeVehicleController"
            };

        public static PhysicsConflictAuditResult AuditAndResolve(
            GameObject vehicleRoot,
            Rigidbody body,
            MonoBehaviour owner)
        {
            var result = new PhysicsConflictAuditResult();
            if (vehicleRoot == null || body == null || owner == null)
            {
                result.fatal = true;
                result.message = "RC3.2 physics owner, vehicle root or Rigidbody is missing.";
                return result;
            }

            RobocraftMotionCoordinator[] owners =
                vehicleRoot.GetComponentsInChildren<RobocraftMotionCoordinator>(true);
            if (owners.Length != 1 || !ReferenceEquals(owners[0], owner))
            {
                result.fatal = true;
                result.message =
                    $"RC3.2 requires exactly one RobocraftMotionCoordinator; found {owners.Length}.";
                return result;
            }

            foreach (MonoBehaviour component in
                     vehicleRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null ||
                    ReferenceEquals(component, owner) ||
                    !LegacyForceOwners.Contains(component.GetType().Name))
                {
                    continue;
                }
                if (component.enabled)
                {
                    component.enabled = false;
                    result.disabledLegacyControllers++;
                }
            }

            body.useGravity = false;
            body.drag = 0f;
            body.angularDrag = 0f;
            body.constraints = RigidbodyConstraints.None;
            Time.fixedDeltaTime = 0.02f;

            Vector3 scale = body.transform.lossyScale;
            if (!Finite(scale) ||
                scale.x <= 0f ||
                scale.y <= 0f ||
                scale.z <= 0f ||
                Mathf.Abs(scale.x - 1f) > 0.001f ||
                Mathf.Abs(scale.y - 1f) > 0.001f ||
                Mathf.Abs(scale.z - 1f) > 0.001f)
            {
                result.fatal = true;
                result.message =
                    $"RC3.2 vehicle Rigidbody must use unit positive world scale; found {scale}.";
                return result;
            }

            Vector3 inertia = body.inertiaTensor;
            if (!Finite(body.mass) ||
                body.mass <= 0f ||
                !Finite(body.centerOfMass) ||
                !Finite(inertia) ||
                inertia.x <= 0f ||
                inertia.y <= 0f ||
                inertia.z <= 0f ||
                !Finite(body.inertiaTensorRotation))
            {
                result.fatal = true;
                result.message = "RC3.2 Rigidbody mass properties are invalid.";
                return result;
            }

            float triangleTolerance =
                Mathf.Max(inertia.x, Mathf.Max(inertia.y, inertia.z)) * 0.001f;
            if (inertia.x > inertia.y + inertia.z + triangleTolerance ||
                inertia.y > inertia.x + inertia.z + triangleTolerance ||
                inertia.z > inertia.x + inertia.y + triangleTolerance)
            {
                result.fatal = true;
                result.message =
                    $"RC3.2 principal inertia violates the rigid-body triangle inequality: {inertia}.";
                return result;
            }

            result.message = result.disabledLegacyControllers > 0
                ? $"RC3.2 disabled {result.disabledLegacyControllers} legacy force controller(s)."
                : "RC3.2 physics ownership is exclusive.";
            return result;
        }

        static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static bool Finite(Vector3 value)
        {
            return Finite(value.x) && Finite(value.y) && Finite(value.z);
        }

        static bool Finite(Quaternion value)
        {
            return Finite(value.x) &&
                   Finite(value.y) &&
                   Finite(value.z) &&
                   Finite(value.w);
        }
    }
}
