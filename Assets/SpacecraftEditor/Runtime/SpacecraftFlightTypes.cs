using System;
using UnityEngine;

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

    [Serializable]
    public sealed class ShipFlightProfile
    {
        [SerializeField] Vector3 rcsAcceleration = new Vector3(8f, 8f, 5f);
        [SerializeField] Vector3 maximumAngularSpeed = new Vector3(75f, 65f, 95f);
        [SerializeField] Vector3 maximumAngularAcceleration = new Vector3(180f, 180f, 180f);
        [SerializeField, Min(0.1f)] float velocityResponse = 3.2f;
        [SerializeField, Min(0.1f)] float angularRateResponse = 7f;
        [SerializeField, Min(1f)] float defaultSpeedLimit = 120f;
        [SerializeField, Min(1f)] float minimumSpeedLimit = 20f;
        [SerializeField, Min(1f)] float maximumSpeedLimit = 300f;
        [SerializeField, Min(1f)] float boostMultiplier = 1.5f;
        [SerializeField, Min(0.1f)] float boostCapacitySeconds = 4f;
        [SerializeField, Min(0.01f)] float boostRegenerationPerSecond = 0.65f;

        public Vector3 RcsAcceleration => Positive(rcsAcceleration, new Vector3(8f, 8f, 5f));
        public Vector3 MaximumAngularSpeed => Positive(maximumAngularSpeed, new Vector3(75f, 65f, 95f));
        public Vector3 MaximumAngularAcceleration => Positive(maximumAngularAcceleration, new Vector3(180f, 180f, 180f));
        public float VelocityResponse => Mathf.Max(0.1f, velocityResponse);
        public float AngularRateResponse => Mathf.Max(0.1f, angularRateResponse);
        public float DefaultSpeedLimit => Mathf.Clamp(defaultSpeedLimit, MinimumSpeedLimit, MaximumSpeedLimit);
        public float MinimumSpeedLimit => Mathf.Max(1f, minimumSpeedLimit);
        public float MaximumSpeedLimit => Mathf.Max(MinimumSpeedLimit, maximumSpeedLimit);
        public float BoostMultiplier => Mathf.Max(1f, boostMultiplier);
        public float BoostCapacitySeconds => Mathf.Max(0.1f, boostCapacitySeconds);
        public float BoostRegenerationPerSecond => Mathf.Max(0.01f, boostRegenerationPerSecond);

        public static ShipFlightProfile CreateForHull(string hullId)
        {
            var profile = new ShipFlightProfile();
            if (!string.IsNullOrEmpty(hullId) && hullId.IndexOf("saucer", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                profile.rcsAcceleration = new Vector3(6f, 6f, 4f);
                profile.maximumAngularSpeed = new Vector3(50f, 45f, 65f);
                profile.maximumAngularAcceleration = new Vector3(120f, 120f, 120f);
                profile.defaultSpeedLimit = 100f;
                profile.maximumSpeedLimit = 240f;
            }
            else if (!string.IsNullOrEmpty(hullId) && hullId.IndexOf("spindle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                profile.rcsAcceleration = new Vector3(10f, 10f, 6f);
                profile.maximumAngularSpeed = new Vector3(95f, 85f, 125f);
                profile.maximumAngularAcceleration = new Vector3(240f, 240f, 240f);
                profile.defaultSpeedLimit = 150f;
                profile.maximumSpeedLimit = 340f;
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
        public float speedLimit;
        public float controlAuthority;
        public float boostRatio;
        public SpacecraftAssistMode assistMode;
    }
}
