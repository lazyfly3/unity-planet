using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace SpacecraftEditor
{
    public enum SpacecraftAerodynamicRole
    {
        None,
        Body,
        Wing,
        Canard,
        HorizontalStabilizer,
        VerticalStabilizer,
        Fairing
    }

    [Serializable]
    public sealed class ShipAerodynamicProfile
    {
        [SerializeField] SpacecraftAerodynamicRole role;
        [SerializeField, Min(0f)] float referenceArea;
        [SerializeField, Min(0f)] float baseDragCoefficient = 0.08f;
        [SerializeField, Min(0f)] float liftSlopePerRadian = 3.2f;
        [SerializeField] float zeroLiftAngleDegrees;
        [SerializeField, Range(5f, 55f)] float stallAngleDegrees = 24f;
        [SerializeField, Min(0f)] float inducedDragFactor = 0.12f;
        [SerializeField, Min(0f)] float sideStabilityCoefficient;
        [SerializeField] Vector3 centerOffset;

        public SpacecraftAerodynamicRole Role => role;
        public float ReferenceArea => Mathf.Max(0f, referenceArea);
        public float BaseDragCoefficient => Mathf.Max(0f, baseDragCoefficient);
        public float LiftSlopePerRadian => Mathf.Max(0f, liftSlopePerRadian);
        public float ZeroLiftAngleDegrees => zeroLiftAngleDegrees;
        public float StallAngleDegrees => Mathf.Clamp(stallAngleDegrees, 5f, 55f);
        public float InducedDragFactor => Mathf.Max(0f, inducedDragFactor);
        public float SideStabilityCoefficient => Mathf.Max(0f, sideStabilityCoefficient);
        public Vector3 CenterOffset => centerOffset;
        public bool IsEnabled => role != SpacecraftAerodynamicRole.None
            && ReferenceArea > 0.0001f;
        public bool ProducesVerticalLift => role == SpacecraftAerodynamicRole.Wing
            || role == SpacecraftAerodynamicRole.Canard
            || role == SpacecraftAerodynamicRole.HorizontalStabilizer;
        public bool ProducesSideForce => role == SpacecraftAerodynamicRole.VerticalStabilizer
            || SideStabilityCoefficient > 0.0001f;

        public float EvaluateLiftCoefficient(float angleOfAttackDegrees)
        {
            if (!ProducesVerticalLift)
                return 0f;

            float angle = angleOfAttackDegrees - ZeroLiftAngleDegrees;
            float absolute = Mathf.Abs(angle);
            float linear = LiftSlopePerRadian * angle * Mathf.Deg2Rad;
            if (absolute <= StallAngleDegrees)
                return linear;

            float atStall = LiftSlopePerRadian
                * StallAngleDegrees
                * Mathf.Deg2Rad
                * Mathf.Sign(angle);
            float decay = 1f - Mathf.Clamp01(
                (absolute - StallAngleDegrees) / 35f);
            return atStall * Mathf.Max(0.12f, decay);
        }

        public float EvaluateDragCoefficient(float liftCoefficient)
        {
            return BaseDragCoefficient
                + InducedDragFactor * liftCoefficient * liftCoefficient;
        }

        public static ShipAerodynamicProfile CreateForHull(
            string hullId,
            Vector3 dimensions)
        {
            float frontalArea = Mathf.Max(
                0.5f,
                Mathf.Abs(dimensions.x * dimensions.y));
            float sideArea = Mathf.Max(
                0.5f,
                Mathf.Abs(dimensions.z * dimensions.y));
            float planformArea = Mathf.Max(
                0.5f,
                Mathf.Abs(dimensions.x * dimensions.z));
            float slenderness = dimensions.z / Mathf.Max(0.1f, dimensions.x);
            return new ShipAerodynamicProfile
            {
                role = SpacecraftAerodynamicRole.Body,
                referenceArea = Mathf.Max(frontalArea, planformArea * 0.2f),
                baseDragCoefficient = Mathf.Lerp(
                    0.2f,
                    0.1f,
                    Mathf.InverseLerp(1f, 3.5f, slenderness)),
                liftSlopePerRadian = 0f,
                inducedDragFactor = 0f,
                sideStabilityCoefficient = sideArea * 0.008f,
                stallAngleDegrees = 24f
            };
        }

        public static ShipAerodynamicProfile CreateForPart(
            string partId,
            SpacecraftPartCategory category)
        {
            if (category != SpacecraftPartCategory.Decoration
                || string.IsNullOrEmpty(partId))
            {
                return new ShipAerodynamicProfile();
            }

            string id = partId.ToLowerInvariant();
            if (id.Contains("delta_wing"))
                return LiftSurface(SpacecraftAerodynamicRole.Wing, 5.2f, 4.1f, 25f, 0.085f);
            if (id.Contains("swept_wing"))
                return LiftSurface(SpacecraftAerodynamicRole.Wing, 4.6f, 3.8f, 27f, 0.075f);
            if (id.Contains("canard"))
                return LiftSurface(SpacecraftAerodynamicRole.Canard, 1.8f, 4.3f, 22f, 0.095f);
            if (id.Contains("vertical_fin"))
            {
                return new ShipAerodynamicProfile
                {
                    role = SpacecraftAerodynamicRole.VerticalStabilizer,
                    referenceArea = 2.2f,
                    baseDragCoefficient = 0.06f,
                    liftSlopePerRadian = 0f,
                    inducedDragFactor = 0.08f,
                    sideStabilityCoefficient = 1.35f,
                    stallAngleDegrees = 28f
                };
            }
            if (id.Contains("fairing"))
            {
                return new ShipAerodynamicProfile
                {
                    role = SpacecraftAerodynamicRole.Fairing,
                    referenceArea = 1.6f,
                    baseDragCoefficient = 0.055f,
                    liftSlopePerRadian = 0f,
                    inducedDragFactor = 0f,
                    sideStabilityCoefficient = 0.08f
                };
            }
            if (id.Contains("nacelle"))
            {
                return new ShipAerodynamicProfile
                {
                    role = SpacecraftAerodynamicRole.Body,
                    referenceArea = 1.3f,
                    baseDragCoefficient = 0.11f,
                    liftSlopePerRadian = 0f,
                    inducedDragFactor = 0f,
                    sideStabilityCoefficient = 0.05f
                };
            }
            if (id.Contains("radiator"))
            {
                return new ShipAerodynamicProfile
                {
                    role = SpacecraftAerodynamicRole.Body,
                    referenceArea = 1.8f,
                    baseDragCoefficient = 0.24f,
                    liftSlopePerRadian = 0f,
                    inducedDragFactor = 0f,
                    sideStabilityCoefficient = 0.04f
                };
            }
            return new ShipAerodynamicProfile();
        }

        static ShipAerodynamicProfile LiftSurface(
            SpacecraftAerodynamicRole aerodynamicRole,
            float area,
            float liftSlope,
            float stallAngle,
            float drag)
        {
            return new ShipAerodynamicProfile
            {
                role = aerodynamicRole,
                referenceArea = area,
                baseDragCoefficient = drag,
                liftSlopePerRadian = liftSlope,
                zeroLiftAngleDegrees = 0f,
                stallAngleDegrees = stallAngle,
                inducedDragFactor = 0.11f,
                sideStabilityCoefficient = 0.025f
            };
        }
    }

    public struct PlanetaryFlightContext
    {
        public bool active;
        public Vector3 upWorld;
        public Vector3 gravityAccelerationWorld;
        public Vector3 aerodynamicAccelerationWorld;
        public Vector3 atmosphereVelocityWorld;
        public float altitude;
        public float airDensity;
        public float airSpeed;
        public float dynamicPressure;
        public float angleOfAttack;
        public bool isStalling;
    }

    public struct SpacecraftDirectionalAuthority
    {
        public Vector3 positiveForce;
        public Vector3 negativeForce;
        public Vector3 positiveTorque;
        public Vector3 negativeTorque;
        public Vector3 positiveAcceleration;
        public Vector3 negativeAcceleration;

        public Vector3 MaximumAcceleration =>
            Vector3.Max(positiveAcceleration, negativeAcceleration);
    }

    public enum SpacecraftAssistMode
    {
        Coupled,
        Decoupled,
        Direct
    }

    public enum InterstellarCruiseState
    {
        Inactive,
        Spooling,
        Accelerating,
        Cruising,
        Decelerating,
        Cooldown
    }

    public enum InterstellarWarpState
    {
        Unlocked,
        Locked,
        Aligning,
        Spooling,
        Transit,
        Exiting,
        Cooldown
    }

    public enum InterstellarWarpCancelReason
    {
        None,
        NoReticleTarget,
        TargetLost,
        Manual,
        Damaged,
        Misaligned,
        ControlsDisabled
    }

    [Serializable]
    public sealed class ShipFlightProfile
    {
        [SerializeField] Vector3 rcsAcceleration = new Vector3(3.5f, 3.5f, 6f);
        [SerializeField] Vector3 integratedRcsNozzleForce = new Vector3(10500f, 10500f, 18000f);
        [SerializeField] Vector3 maximumAngularSpeed = new Vector3(75f, 65f, 95f);
        [SerializeField] Vector3 maximumAngularAcceleration = new Vector3(180f, 180f, 180f);
        [SerializeField, Min(0.1f)] float velocityResponse = 3.2f;
        [SerializeField, Min(0.1f)] float angularRateResponse = 7f;
        [FormerlySerializedAs("defaultSpeedLimit")]
        [SerializeField, Min(1f)] float defaultTargetSpeed = 250f;
        [FormerlySerializedAs("minimumSpeedLimit")]
        [SerializeField, Min(1f)] float minimumTargetSpeed = 25f;
        [FormerlySerializedAs("maximumSpeedLimit")]
        [SerializeField, Min(1f)] float maximumTargetSpeed = 10000f;
        [SerializeField, Min(1f)] float boostMultiplier = 1.5f;
        [SerializeField, Min(0.1f)] float boostCapacitySeconds = 4f;
        [SerializeField, Min(0.01f)] float boostRegenerationPerSecond = 0.65f;

        public Vector3 RcsAcceleration => Positive(rcsAcceleration, new Vector3(3.5f, 3.5f, 6f));
        public Vector3 IntegratedRcsNozzleForce => Positive(
            integratedRcsNozzleForce,
            new Vector3(10500f, 10500f, 18000f));
        public Vector3 MaximumAngularSpeed => Positive(maximumAngularSpeed, new Vector3(75f, 65f, 95f));
        public Vector3 MaximumAngularAcceleration => Positive(maximumAngularAcceleration, new Vector3(180f, 180f, 180f));
        public float VelocityResponse => Mathf.Max(0.1f, velocityResponse);
        public float AngularRateResponse => Mathf.Max(0.1f, angularRateResponse);
        public float DefaultTargetSpeed => Mathf.Clamp(
            defaultTargetSpeed,
            MinimumTargetSpeed,
            MaximumTargetSpeed);
        public float MinimumTargetSpeed => Mathf.Max(1f, minimumTargetSpeed);
        public float MaximumTargetSpeed => Mathf.Max(MinimumTargetSpeed, maximumTargetSpeed);
        [Obsolete("Use DefaultTargetSpeed.")]
        public float DefaultSpeedLimit => DefaultTargetSpeed;
        [Obsolete("Use MinimumTargetSpeed.")]
        public float MinimumSpeedLimit => MinimumTargetSpeed;
        [Obsolete("Use MaximumTargetSpeed.")]
        public float MaximumSpeedLimit => MaximumTargetSpeed;
        public float BoostMultiplier => Mathf.Max(1f, boostMultiplier);
        public float BoostCapacitySeconds => Mathf.Max(0.1f, boostCapacitySeconds);
        public float BoostRegenerationPerSecond => Mathf.Max(0.01f, boostRegenerationPerSecond);

        public static ShipFlightProfile CreateForHull(string hullId)
        {
            var profile = new ShipFlightProfile();
            if (!string.IsNullOrEmpty(hullId) && hullId.IndexOf("saucer", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                profile.rcsAcceleration = new Vector3(3.44f, 3.44f, 6f);
                profile.integratedRcsNozzleForce = new Vector3(15500f, 15500f, 27000f);
                profile.maximumAngularSpeed = new Vector3(50f, 45f, 65f);
                profile.maximumAngularAcceleration = new Vector3(120f, 120f, 120f);
                profile.defaultTargetSpeed = 250f;
                profile.maximumTargetSpeed = 10000f;
            }
            else if (!string.IsNullOrEmpty(hullId) && hullId.IndexOf("spindle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                profile.rcsAcceleration = new Vector3(3.47f, 3.47f, 5.87f);
                profile.integratedRcsNozzleForce = new Vector3(7800f, 7800f, 13200f);
                profile.maximumAngularSpeed = new Vector3(95f, 85f, 125f);
                profile.maximumAngularAcceleration = new Vector3(240f, 240f, 240f);
                profile.defaultTargetSpeed = 250f;
                profile.maximumTargetSpeed = 10000f;
            }
            return profile;
        }

        static Vector3 Positive(Vector3 value, Vector3 fallback)
        {
            return new Vector3(
                value.x > 0f ? value.x : fallback.x,
                value.y > 0f ? value.y : fallback.y,
                value.z > 0f ? value.z : fallback.z);
        }
    }

    public struct SpacecraftFlightCommand
    {
        public Vector3 translation;
        public Vector2 vjoy;
        public float roll;
        public bool brake;
        public bool boost;

        public bool HasMotionInput => translation.sqrMagnitude > 0.0001f
            || vjoy.sqrMagnitude > 0.0001f
            || Mathf.Abs(roll) > 0.0001f;
    }

    public interface ISpacecraftFlightCommandSource
    {
        SpacecraftFlightCommand Command { get; }
    }

    public interface ISpacecraftPilotControlSource
    {
        bool ConsumeCoupledToggle();
        bool ConsumeDirectToggle();
        bool ConsumeCruisePressed();
        float ConsumeSpeedLimitDelta();
        void ResetVJoy();
        void ClearTransientRequests();
    }

    public struct SpacecraftControlTelemetry
    {
        public Vector3 localVelocity;
        public Vector3 localAngularVelocity;
        public Vector3 requestedLocalForce;
        public Vector3 requestedLocalTorque;
        public Vector3 appliedLocalForce;
        public Vector3 currentAcceleration;
        public float shipMass;
        public float targetSpeed;
        public float speedLimit;
        public float controlAuthority;
        public float boostRatio;
        public SpacecraftAssistMode assistMode;
        public SpacecraftDirectionalAuthority directionalAuthority;
        public bool planetaryFlightActive;
        public float verticalSpeed;
        public Vector3 gravitySupportAcceleration;
        public float gravitySupportLoad;
        public float airDensity;
        public float airSpeed;
        public float dynamicPressure;
        public float angleOfAttack;
        public bool isStalling;
    }
}
