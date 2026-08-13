using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UnityPlanet.CityPcg
{
    /// <summary>
    /// Non-blocking gameplay contact for a generated cable bundle. Every
    /// collider is a trigger, and the slowdown is a centred linear impulse:
    /// no mass, drag, constraint, angular velocity or vehicle module changes.
    /// The existing whole-vehicle force gateway is used when it is available.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AerialCableSlowHazard : MonoBehaviour
    {
        const string ExternalForcesTypeName =
            "UnityPlanet.ModularAssembly.VehicleExternalForces";

        static MethodInfo applyImpulseMethod;
        static bool searchedForImpulseMethod;

        readonly Dictionary<int, float> nextAllowedHitAt =
            new Dictionary<int, float>(16);
        AerialCableCurve[] curves = Array.Empty<AerialCableCurve>();
        int triggerSegmentsPerCurve = 6;
        float triggerRadius = 1.05f;
        float minimumAffectedSpeed = 8f;
        float playerRetainedSpeed = 0.72f;
        float enemyRetainedSpeed = 0.86f;
        float repeatCooldown = 0.45f;
        float playerSwayAmplitude = 1.55f;
        float enemySwayAmplitude = 0.85f;
        float swayDuration = 1.05f;

        public int TriggerCount { get; private set; }

        public void Configure(
            AerialCableCurve[] cableCurves,
            AirCombatCitySettings citySettings)
        {
            curves = cableCurves ?? Array.Empty<AerialCableCurve>();
            AirCombatCitySettings source = citySettings ??
                                           new AirCombatCitySettings();
            triggerSegmentsPerCurve = Mathf.Clamp(
                source.cableTriggerSegmentsPerCurve,
                1,
                16);
            triggerRadius = Mathf.Max(0.05f, source.cableTriggerRadius);
            minimumAffectedSpeed = Mathf.Max(
                0f,
                source.cableMinimumAffectedSpeed);
            playerRetainedSpeed = 1f - Mathf.Clamp(
                source.playerCableSlowdown,
                0f,
                0.9f);
            enemyRetainedSpeed = 1f - Mathf.Clamp(
                source.enemyCableSlowdown,
                0f,
                0.9f);
            repeatCooldown = Mathf.Max(0.05f, source.cableRepeatCooldown);
            playerSwayAmplitude = Mathf.Max(
                0f,
                source.playerCableSwayAmplitude);
            enemySwayAmplitude = Mathf.Max(
                0f,
                source.enemyCableSwayAmplitude);
            swayDuration = Mathf.Max(0.05f, source.cableSwayDuration);
            ClearTriggerSegments();
            for (int curveIndex = 0; curveIndex < curves.Length; curveIndex++)
                BuildCurveTriggers(curves[curveIndex], curveIndex);
        }

        void BuildCurveTriggers(AerialCableCurve curve, int curveIndex)
        {
            if (curve == null || curve.PointCount < 2)
                return;

            int lastPoint = curve.PointCount - 1;
            for (int segment = 0;
                 segment < triggerSegmentsPerCurve;
                 segment++)
            {
                int firstIndex = Mathf.RoundToInt(
                    segment * lastPoint / (float)triggerSegmentsPerCurve);
                int secondIndex = Mathf.RoundToInt(
                    (segment + 1) * lastPoint /
                    (float)triggerSegmentsPerCurve);
                if (secondIndex <= firstIndex)
                    continue;

                Vector3 start = curve.GetWorldPoint(firstIndex);
                Vector3 end = curve.GetWorldPoint(secondIndex);
                Vector3 delta = end - start;
                float length = delta.magnitude;
                if (length <= 0.05f)
                    continue;

                var segmentObject = new GameObject(
                    "CableSlowTrigger_" + curveIndex.ToString("D1") + "_" +
                    segment.ToString("D2"));
                segmentObject.layer = gameObject.layer;
                segmentObject.transform.SetParent(transform, true);
                segmentObject.transform.position = (start + end) * 0.5f;
                segmentObject.transform.rotation = Quaternion.FromToRotation(
                    Vector3.up,
                    delta / length);

                var trigger = segmentObject.AddComponent<CapsuleCollider>();
                trigger.isTrigger = true;
                trigger.direction = 1;
                trigger.radius = triggerRadius;
                trigger.height = length + triggerRadius * 2f;
                segmentObject.AddComponent<AerialCableContactRelay>()
                    .Configure(this);
                TriggerCount++;
            }
        }

        void ClearTriggerSegments()
        {
            TriggerCount = 0;
            for (int childIndex = transform.childCount - 1;
                 childIndex >= 0;
                 childIndex--)
            {
                Transform child = transform.GetChild(childIndex);
                if (!child.name.StartsWith(
                        "CableSlowTrigger_",
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        internal void HandleContact(Collider other, Vector3 contactPoint)
        {
            if (!Application.isPlaying || other == null)
                return;
            Rigidbody body = other.attachedRigidbody;
            if (body == null || body.isKinematic)
                return;

            bool enemy = body.GetComponent("HordeEnemyVehicle") != null;
            bool player = !enemy &&
                (body.GetComponent("RobocraftMotionCoordinator") != null ||
                 body.GetComponent("GridFlightBridge") != null);
            if (!enemy && !player)
                return;

            int bodyId = body.GetInstanceID();
            if (nextAllowedHitAt.TryGetValue(bodyId, out float nextAllowed) &&
                Time.time < nextAllowed)
            {
                return;
            }

            Vector3 velocity = body.velocity;
            float speed = velocity.magnitude;
            if (speed < minimumAffectedSpeed)
                return;

            float retainedSpeed = enemy
                ? enemyRetainedSpeed
                : playerRetainedSpeed;
            Vector3 deltaVelocity = velocity * (retainedSpeed - 1f);
            Vector3 impulse = deltaVelocity * Mathf.Max(0.01f, body.mass);
            ApplyCentredImpulse(body, impulse);
            nextAllowedHitAt[bodyId] = Time.time + repeatCooldown;

            float visualAmplitude = enemy
                ? enemySwayAmplitude
                : playerSwayAmplitude;
            for (int index = 0; index < curves.Length; index++)
            {
                curves[index]?.PlayImpact(
                    contactPoint,
                    velocity / speed,
                    visualAmplitude,
                    swayDuration);
            }
        }

        public static Vector3 CalculateVelocityAfterContact(
            Vector3 velocity,
            bool enemy)
        {
            return CalculateVelocityAfterContact(
                velocity,
                enemy,
                0.28f,
                0.14f,
                8f);
        }

        public static Vector3 CalculateVelocityAfterContact(
            Vector3 velocity,
            bool enemy,
            float playerSlowdown,
            float enemySlowdown,
            float minimumSpeed)
        {
            if (velocity.magnitude < Mathf.Max(0f, minimumSpeed))
                return velocity;
            float slowdown = enemy ? enemySlowdown : playerSlowdown;
            return velocity * (1f - Mathf.Clamp(slowdown, 0f, 0.9f));
        }

        static void ApplyCentredImpulse(Rigidbody body, Vector3 impulse)
        {
            MethodInfo method = ResolveExternalImpulseMethod();
            if (method != null)
            {
                try
                {
                    method.Invoke(
                        null,
                        new object[]
                        {
                            body,
                            impulse,
                            body.worldCenterOfMass
                        });
                    return;
                }
                catch (Exception)
                {
                    // The fallback below remains a centred velocity change and
                    // therefore still cannot introduce angular impulse.
                }
            }
            body.AddForce(
                impulse / Mathf.Max(0.01f, body.mass),
                ForceMode.VelocityChange);
        }

        static MethodInfo ResolveExternalImpulseMethod()
        {
            if (searchedForImpulseMethod)
                return applyImpulseMethod;
            searchedForImpulseMethod = true;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                Type type = assemblies[index].GetType(
                    ExternalForcesTypeName,
                    false);
                if (type == null)
                    continue;
                applyImpulseMethod = type.GetMethod(
                    "ApplyImpulseInCurrentPhysicsStep",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[]
                    {
                        typeof(Rigidbody),
                        typeof(Vector3),
                        typeof(Vector3)
                    },
                    null);
                break;
            }
            return applyImpulseMethod;
        }
    }

    [DisallowMultipleComponent]
    public sealed class AerialCableContactRelay : MonoBehaviour
    {
        AerialCableSlowHazard owner;

        public void Configure(AerialCableSlowHazard target)
        {
            owner = target;
        }

        void OnTriggerEnter(Collider other)
        {
            owner?.HandleContact(other, transform.position);
        }
    }
}
