using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace SpacecraftEditor
{
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
    }
}
