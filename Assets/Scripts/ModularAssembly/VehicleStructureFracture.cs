using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    [Serializable]
    public sealed class DetachedComponentSnapshot
    {
        public readonly List<string> RuntimeIds = new List<string>();
        public float MassKg;
        public Vector3 WorldCenter;
        public Vector3 WorldLinearVelocity;
        public Vector3 WorldAngularVelocity;
        public Vector3 PrincipalInertia;
        public Quaternion PrincipalAxes = Quaternion.identity;
        public bool IsDirectHit;
    }

    [Serializable]
    public sealed class VehicleStructureDelta
    {
        public string DirectHitRuntimeId = string.Empty;
        public readonly List<string> RemovedRuntimeIds = new List<string>();
        public readonly List<string> RemainingRuntimeIds = new List<string>();
        public readonly List<DetachedComponentSnapshot> DetachedComponents =
            new List<DetachedComponentSnapshot>();
        public DetachedComponentSnapshot DirectDestroyedComponent;
        public Vector3 HitPoint;
        public Vector3 Impulse;
        public bool CoreDestroyed;

        public static VehicleStructureDelta Initial(
            IEnumerable<string> remaining)
        {
            var result = new VehicleStructureDelta();
            if (remaining != null)
                result.RemainingRuntimeIds.AddRange(remaining);
            return result;
        }
    }

    public readonly struct DetachedDebrisPart
    {
        public readonly string RuntimeId;
        public readonly GameObject Source;
        public readonly float MassKg;

        public DetachedDebrisPart(
            string runtimeId,
            GameObject source,
            float massKg)
        {
            RuntimeId = runtimeId;
            Source = source;
            MassKg = Mathf.Max(0.01f, massKg);
        }
    }

    public static class VehicleDetachedDebris
    {
        const int MaxClusters = 24;
        const float DetachedLifetimeSeconds = 2.6f;
        const float DirectBreakLifetimeSeconds = 2.2f;
        const float MaximumAngularSpeed = 7f;

        enum DebrisMotionKind
        {
            Detached,
            DirectBreak
        }

        static readonly Queue<GameObject> Active =
            new Queue<GameObject>();

        public static void ClearAll()
        {
            while (Active.Count > 0)
            {
                GameObject active = Active.Dequeue();
                if (active != null)
                    UnityEngine.Object.Destroy(active);
            }

            Transform transientRoot = CombatTransientRoot.GetOrCreate();
            if (transientRoot == null)
                return;
            for (int index = transientRoot.childCount - 1; index >= 0; index--)
            {
                Transform child = transientRoot.GetChild(index);
                if (child != null &&
                    child.name.StartsWith(
                        "DetachedModuleCluster",
                        StringComparison.Ordinal))
                    UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        public static GameObject Spawn(
            IEnumerable<DetachedDebrisPart> sourceParts,
            Rigidbody sourceBody,
            Vector3 impulse,
            Vector3 hitPoint)
        {
            return SpawnInternal(
                sourceParts,
                sourceBody,
                impulse,
                hitPoint,
                DebrisMotionKind.Detached);
        }

        public static GameObject SpawnDirectBreak(
            IEnumerable<DetachedDebrisPart> sourceParts,
            Rigidbody sourceBody,
            Vector3 impulse,
            Vector3 hitPoint)
        {
            return SpawnInternal(
                sourceParts,
                sourceBody,
                impulse,
                hitPoint,
                DebrisMotionKind.DirectBreak);
        }

        static GameObject SpawnInternal(
            IEnumerable<DetachedDebrisPart> sourceParts,
            Rigidbody sourceBody,
            Vector3 impulse,
            Vector3 hitPoint,
            DebrisMotionKind motionKind)
        {
            List<DetachedDebrisPart> parts = sourceParts?
                .Where(item => item.Source != null)
                .ToList();
            if (parts == null || parts.Count == 0)
                return null;

            Vector3 center = Vector3.zero;
            float totalMass = 0f;
            foreach (DetachedDebrisPart part in parts)
            {
                float mass = Mathf.Max(0.01f, part.MassKg);
                center += part.Source.transform.position * mass;
                totalMass += mass;
            }
            center /= Mathf.Max(0.01f, totalMass);

            var root = new GameObject("DetachedModuleCluster");
            root.transform.SetParent(
                CombatTransientRoot.GetOrCreate(),
                false);
            root.transform.SetPositionAndRotation(
                center,
                Quaternion.identity);
            SetLayerRecursively(root, 2);

            bool hasBounds = false;
            Bounds worldBounds = default;
            foreach (DetachedDebrisPart part in parts)
            {
                GameObject clone = UnityEngine.Object.Instantiate(
                    part.Source);
                clone.name = "Debris_" + part.RuntimeId;
                clone.transform.SetParent(root.transform, true);
                SetLayerRecursively(clone, 2);
                foreach (MonoBehaviour behaviour in
                         clone.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour != null)
                        behaviour.enabled = false;
                foreach (Collider collider in
                         clone.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
                foreach (Rigidbody nestedBody in
                         clone.GetComponentsInChildren<Rigidbody>(true))
                {
                    nestedBody.isKinematic = true;
                    nestedBody.detectCollisions = false;
                }
                foreach (Renderer renderer in
                         clone.GetComponentsInChildren<Renderer>(true))
                {
                    if (!hasBounds)
                    {
                        worldBounds = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                        worldBounds.Encapsulate(renderer.bounds);
                }
            }

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.drag = 0f;
            body.angularDrag = 0f;
            body.mass = Mathf.Max(1f, totalMass);
            body.detectCollisions = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
            body.maxAngularVelocity = MaximumAngularSpeed;
            if (sourceBody != null)
            {
                body.velocity = sourceBody.GetPointVelocity(center);
                body.angularVelocity = sourceBody.angularVelocity;
            }
            ApplyVisualBreakMotion(
                body,
                sourceBody,
                parts,
                center,
                totalMass,
                impulse,
                hitPoint,
                motionKind);
            float lifetime = motionKind == DebrisMotionKind.DirectBreak
                ? DirectBreakLifetimeSeconds
                : DetachedLifetimeSeconds;
            root.AddComponent<VehicleDetachedDebrisLifetime>()
                .Initialize(lifetime, hasBounds ? worldBounds : default);

            Active.Enqueue(root);
            while (Active.Count > MaxClusters)
            {
                GameObject oldest = Active.Dequeue();
                if (oldest != null)
                    UnityEngine.Object.Destroy(oldest);
            }
            return root;
        }

        static void ApplyVisualBreakMotion(
            Rigidbody debrisBody,
            Rigidbody sourceBody,
            IReadOnlyList<DetachedDebrisPart> parts,
            Vector3 center,
            float totalMass,
            Vector3 impulse,
            Vector3 hitPoint,
            DebrisMotionKind motionKind)
        {
            Vector3 outward = sourceBody == null
                ? center - hitPoint
                : center - sourceBody.worldCenterOfMass;
            if (outward.sqrMagnitude < 0.0001f)
                outward = Vector3.up;
            outward.Normalize();

            Vector3 impactDirection = impulse.sqrMagnitude > 0.0001f
                ? impulse.normalized
                : outward;
            Vector3 kickDirection = motionKind ==
                                    DebrisMotionKind.DirectBreak
                ? impactDirection * 0.8f + outward * 0.55f
                : impactDirection * 0.3f + outward;
            if (kickDirection.sqrMagnitude < 0.0001f)
                kickDirection = outward;
            kickDirection.Normalize();

            Vector3 scatter = ResolveStableScatter(parts, kickDirection);
            float scatterWeight = motionKind ==
                                  DebrisMotionKind.DirectBreak
                ? 0.16f
                : 0.1f;
            kickDirection = (kickDirection + scatter * scatterWeight)
                .normalized;

            float equivalentImpactSpeed = impulse.magnitude /
                                          Mathf.Max(1f, totalMass);
            float massResponse = Mathf.Clamp(
                Mathf.Sqrt(80f / Mathf.Max(20f, totalMass)),
                0.72f,
                1.18f);
            float kickSpeed = motionKind == DebrisMotionKind.DirectBreak
                ? Mathf.Clamp(
                    4.5f + Mathf.Sqrt(equivalentImpactSpeed) * 2.4f,
                    4.5f,
                    11f)
                : Mathf.Clamp(
                    1.8f + Mathf.Sqrt(equivalentImpactSpeed) * 1.25f,
                    1.8f,
                    5.5f);
            kickSpeed *= massResponse;
            debrisBody.velocity += kickDirection * kickSpeed;

            Vector3 lever = hitPoint - center;
            Vector3 tumbleAxis = Vector3.Cross(lever, kickDirection);
            if (tumbleAxis.sqrMagnitude < 0.0001f)
                tumbleAxis = Vector3.Cross(kickDirection, scatter);
            if (tumbleAxis.sqrMagnitude < 0.0001f)
                tumbleAxis = scatter;
            tumbleAxis.Normalize();
            float tumbleSpeed = motionKind == DebrisMotionKind.DirectBreak
                ? 3.2f
                : 1.6f;
            debrisBody.angularVelocity = Vector3.ClampMagnitude(
                debrisBody.angularVelocity + tumbleAxis *
                (tumbleSpeed * massResponse),
                MaximumAngularSpeed);
        }

        static Vector3 ResolveStableScatter(
            IReadOnlyList<DetachedDebrisPart> parts,
            Vector3 direction)
        {
            uint hash = 2166136261u;
            for (int index = 0; index < parts.Count; index++)
            {
                string id = parts[index].RuntimeId ?? string.Empty;
                for (int character = 0; character < id.Length; character++)
                {
                    hash ^= id[character];
                    hash *= 16777619u;
                }
            }
            float angle = (hash & 0xffffu) / 65535f * Mathf.PI * 2f;
            Vector3 reference = Mathf.Abs(Vector3.Dot(
                direction,
                Vector3.up)) < 0.9f
                ? Vector3.up
                : Vector3.right;
            Vector3 tangent = Vector3.Cross(direction, reference).normalized;
            Vector3 bitangent = Vector3.Cross(direction, tangent).normalized;
            return tangent * Mathf.Cos(angle) +
                   bitangent * Mathf.Sin(angle);
        }

        static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null)
                return;
            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursively(child.gameObject, layer);
        }
    }

    public sealed class VehicleDetachedDebrisLifetime : MonoBehaviour
    {
        Rigidbody body;
        float destroyAt;
        float referenceArea = 1f;

        public void Initialize(float lifetime, Bounds worldBounds)
        {
            body = GetComponent<Rigidbody>();
            Vector3 size = worldBounds.size;
            if (size.sqrMagnitude > 0.0001f)
            {
                referenceArea = Mathf.Max(
                    0.1f,
                    Mathf.Max(size.x * size.y,
                        Mathf.Max(size.x * size.z, size.y * size.z)));
            }
            destroyAt = Time.time + Mathf.Max(0.1f, lifetime);
        }

        void FixedUpdate()
        {
            if (body == null || body.isKinematic)
                return;
            PlanetEnvironmentSample sample =
                PlanetEnvironmentRuntime.Sample(
                    body.worldCenterOfMass,
                    Time.fixedTimeAsDouble);
            body.AddForce(
                sample.gravityAcceleration,
                ForceMode.Acceleration);
            if (!sample.hasAtmosphere || sample.airDensity <= 0.0001f)
                return;
            Vector3 relativeVelocity =
                body.GetPointVelocity(body.worldCenterOfMass) -
                sample.atmosphereVelocity;
            float speed = relativeVelocity.magnitude;
            if (speed <= 0.01f)
                return;
            Vector3 drag =
                -relativeVelocity.normalized *
                (0.5f * sample.airDensity * 0.8f *
                 referenceArea * speed * speed);
            body.AddForce(drag, ForceMode.Force);
        }

        void Update()
        {
            if (Time.time >= destroyAt)
                Destroy(gameObject);
        }
    }
}
