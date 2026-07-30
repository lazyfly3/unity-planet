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
        const float LifetimeSeconds = 1.2f;
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

            if (hasBounds)
            {
                var collider = root.AddComponent<BoxCollider>();
                collider.center =
                    root.transform.InverseTransformPoint(worldBounds.center);
                collider.size = worldBounds.size;
            }

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.drag = 0f;
            body.angularDrag = 0f;
            body.mass = Mathf.Max(1f, totalMass);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
            body.maxAngularVelocity = 1000f;
            if (sourceBody != null)
            {
                body.velocity = sourceBody.GetPointVelocity(center);
                body.angularVelocity = sourceBody.angularVelocity;
            }
            if (impulse.sqrMagnitude > 0.0001f)
            {
                Vector3 forcePoint = hitPoint.sqrMagnitude > 0.0001f
                    ? hitPoint
                    : center;
                body.AddForceAtPosition(
                    impulse,
                    forcePoint,
                    ForceMode.Impulse);
            }
            root.AddComponent<VehicleDetachedDebrisLifetime>()
                .Initialize(LifetimeSeconds);

            Active.Enqueue(root);
            while (Active.Count > MaxClusters)
            {
                GameObject oldest = Active.Dequeue();
                if (oldest != null)
                    UnityEngine.Object.Destroy(oldest);
            }
            return root;
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

        public void Initialize(float lifetime)
        {
            body = GetComponent<Rigidbody>();
            Collider collider = GetComponent<Collider>();
            if (collider != null)
            {
                Vector3 size = collider.bounds.size;
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
