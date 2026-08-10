using UnityEngine;

namespace UnityPlanet.CityPcg
{
    public readonly struct UrbanVehicleImpactData
    {
        public readonly Vector3 point;
        public readonly Vector3 direction;
        public readonly float impactSpeed;
        public readonly float impulseMagnitude;
        public readonly GameObject source;

        public UrbanVehicleImpactData(
            Vector3 point,
            Vector3 direction,
            float impactSpeed,
            float impulseMagnitude,
            GameObject source)
        {
            this.point = point;
            this.direction = direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : Vector3.down;
            this.impactSpeed = Mathf.Max(0f, impactSpeed);
            this.impulseMagnitude = Mathf.Max(0f, impulseMagnitude);
            this.source = source;
        }
    }

    public interface IUrbanVehicleImpactReceiver
    {
        bool ApplyUrbanFallingImpact(in UrbanVehicleImpactData impact);
    }

    public static class UrbanVehicleImpactPolicy
    {
        public const float MinimumVehicleImpactSpeed = 7.5f;

        public static float ResolveModuleDamage(
            float maximumIntegrity,
            float impactSpeed,
            float impulseMagnitude)
        {
            if (impactSpeed < MinimumVehicleImpactSpeed)
                return 0f;
            float speed01 = Mathf.InverseLerp(8f, 46f, impactSpeed);
            float impulse01 = Mathf.InverseLerp(300f, 9000f, impulseMagnitude);
            float fraction = Mathf.Lerp(
                0.18f,
                0.72f,
                Mathf.Max(speed01, impulse01 * 0.82f));
            return Mathf.Clamp(
                Mathf.Max(1f, maximumIntegrity) * fraction,
                16f,
                340f);
        }

        public static bool TryApplyModuleDamage(
            Collider first,
            Collider second,
            Transform fallingRoot,
            Vector3 point,
            Vector3 direction,
            float impactSpeed,
            float impulseMagnitude,
            GameObject source)
        {
            if (!TryResolveReceiver(
                    first,
                    fallingRoot,
                    out IUrbanVehicleImpactReceiver receiver) &&
                !TryResolveReceiver(
                    second,
                    fallingRoot,
                    out receiver))
            {
                return false;
            }
            return receiver.ApplyUrbanFallingImpact(
                new UrbanVehicleImpactData(
                    point,
                    direction,
                    impactSpeed,
                    impulseMagnitude,
                    source));
        }

        static bool TryResolveReceiver(
            Collider collider,
            Transform fallingRoot,
            out IUrbanVehicleImpactReceiver receiver)
        {
            receiver = null;
            if (collider == null ||
                (fallingRoot != null &&
                 collider.transform.IsChildOf(fallingRoot)))
            {
                return false;
            }
            receiver = collider.GetComponentInParent<
                IUrbanVehicleImpactReceiver>();
            return receiver != null;
        }
    }

    /// <summary>
    /// Keeps generated compound colliders outside scaled/imported art roots.
    /// It deliberately has no collision-damage callback: ordinary player and
    /// minion contacts remain physical without deleting facade cells. Boss ram
    /// destruction is routed explicitly while the authored skill is active.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UrbanStructuralColliderProxy : MonoBehaviour
    {
        UrbanDestructibleBuilding owner;

        public UrbanDestructibleBuilding Owner => owner;

        public void Configure(UrbanDestructibleBuilding target)
        {
            owner = target;
        }

    }

    /// <summary>
    /// A detached connected island remains one bounded rigid body. Its collision
    /// may damage nearby structural cells once, then it sleeps as persistent cover.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UrbanStructuralClusterMotion : MonoBehaviour
    {
        Rigidbody body;
        Bounds initialBounds;
        GameObject sourceBuilding;
        float settleAt;
        float lastCascadeAt = -100f;
        float lastVehicleImpactAt = -100f;
        Vector3 debugVelocity;
        Vector3 debugAngularVelocity;
        bool configured;

        public void Configure(
            Rigidbody targetBody,
            Bounds targetBounds,
            GameObject targetSourceBuilding,
            Vector3 initialVelocity,
            Vector3 initialAngularVelocity)
        {
            body = targetBody;
            initialBounds = targetBounds;
            sourceBuilding = targetSourceBuilding;
            debugVelocity = initialVelocity;
            debugAngularVelocity = initialAngularVelocity;
            settleAt = Time.unscaledTime + 7f;
            configured = true;
        }

        void FixedUpdate()
        {
            if (!configured || body == null || body.isKinematic)
                return;
            // A little extra acceleration prevents large sections from reading
            // as weightless without changing global project gravity.
            body.AddForce(Physics.gravity * 0.32f, ForceMode.Acceleration);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!configured || collision == null || collision.contactCount == 0 ||
                collision.gameObject == sourceBuilding)
            {
                return;
            }
            float speed = collision.relativeVelocity.magnitude;
            ContactPoint contact = collision.GetContact(0);
            if (Time.unscaledTime - lastVehicleImpactAt >= 0.45f &&
                UrbanVehicleImpactPolicy.TryApplyModuleDamage(
                    contact.thisCollider,
                    contact.otherCollider,
                    transform,
                    contact.point,
                    collision.relativeVelocity,
                    speed,
                    collision.impulse.magnitude,
                    gameObject))
            {
                lastVehicleImpactAt = Time.unscaledTime;
            }
            if (speed < 11f ||
                Time.unscaledTime - lastCascadeAt < 0.55f)
            {
                return;
            }
            lastCascadeAt = Time.unscaledTime;
            float radius = Mathf.Clamp(
                initialBounds.extents.magnitude * 0.28f,
                3.5f,
                16f);
            float damage = Mathf.Clamp(speed * 2.6f, 28f, 150f);
            UrbanDestructionWorld.ApplyStructuralImpact(
                contact.point,
                radius,
                damage,
                gameObject);
        }

        void Update()
        {
            if (!configured || body == null || body.isKinematic ||
                Time.unscaledTime < settleAt)
            {
                return;
            }
            if (body.velocity.sqrMagnitude > 0.30f ||
                body.angularVelocity.sqrMagnitude > 0.08f)
            {
                settleAt = Time.unscaledTime + 1.2f;
                return;
            }
            body.Sleep();
        }

        public void DebugAdvance(float seconds)
        {
            if (Application.isPlaying || !configured || seconds <= 0f)
                return;
            Vector3 gravity = Physics.gravity * 1.32f;
            transform.position += debugVelocity * seconds +
                                  gravity * (0.5f * seconds * seconds);
            debugVelocity += gravity * seconds;
            float angularSpeed = debugAngularVelocity.magnitude;
            if (angularSpeed > 0.001f)
            {
                transform.rotation = Quaternion.AngleAxis(
                    angularSpeed * Mathf.Rad2Deg * seconds,
                    debugAngularVelocity / angularSpeed) * transform.rotation;
            }
        }
    }
}
