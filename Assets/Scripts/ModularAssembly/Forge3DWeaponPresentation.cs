using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    public static class Forge3DWeaponPresentation
    {
        const string ResourceRoot = "WeaponEffects/Forge3D/";

        public static bool TrySpawnMuzzle(
            Transform host,
            Vector3 position,
            Vector3 direction,
            string sourceEffect,
            Transform weaponRoot)
        {
            string key = Normalize(sourceEffect);
            string resource = null;
            float lifetime = 0.12f;
            float scale = 1f;

            if (key.Contains("gatlin") || key.Contains("machinegun"))
            {
                resource = "VulcanMuzzle";
                bool gatling = key.Contains("gatlin");
                lifetime = gatling ? 0.18f : 0.12f;
                scale = gatling ? 0.34f : 0.18f;
            }
            else if (key.Contains("antiair"))
            {
                resource = "SoloMuzzle";
                scale = 0.22f;
            }
            else if (key.Contains("forge3dfightermissilemuzzle"))
            {
                resource = "MissileFlame";
                lifetime = 0.22f;
                scale = 0.2f;
            }
            else if (key.Contains("forge3dguidedmissilemuzzle"))
            {
                resource = "SeekerMuzzle";
                lifetime = 0.3f;
                scale = 0.18f;
            }
            else if (key.Contains("forge3dmissilemuzzle"))
            {
                resource = "MissileFlame";
                lifetime = 0.2f;
                scale = 0.16f;
            }
            else if (key.Contains("sniper"))
            {
                resource = "SniperImpact";
                lifetime = 0.18f;
                scale = 0.18f;
            }
            else if (key.Contains("energy_cannon"))
            {
                resource = "PlasmaMuzzle";
                lifetime = 0.2f;
                scale = 0.22f;
            }

            if (resource == null)
                return false;

            ResolvePool(host).SpawnOneShot(
                ResourceRoot + resource,
                position,
                direction,
                lifetime,
                scale);
            Forge3DWeaponAnimator.Trigger(
                weaponRoot,
                direction,
                key);
            return true;
        }

        public static bool TrySpawnImpact(
            Transform host,
            Vector3 position,
            Vector3 normal,
            string sourceEffect,
            float radius)
        {
            string key = Normalize(sourceEffect);
            string resource = null;
            float lifetime = 0.8f;
            float scale = 1f;

            if (key.Contains("gatlin") || key.Contains("machinegun"))
            {
                resource = "VulcanImpact";
                lifetime = 0.55f;
                scale = 0.12f;
            }
            else if (key.Contains("antiair"))
            {
                resource = "ExplosionSmall";
                lifetime = 1.2f;
                scale = Mathf.Clamp(
                    radius > 0.1f ? radius * 0.055f : 0.07f,
                    0.05f,
                    0.12f);
            }
            else if (key.Contains("sniper"))
            {
                resource = "SniperImpact";
                lifetime = 0.7f;
                scale = 0.16f;
            }
            else if (key.Contains("energy_cannon"))
            {
                resource = "PlasmaImpact";
                lifetime = 0.9f;
                scale = Mathf.Clamp(
                    radius > 0.1f ? radius * 0.08f : 0.1f,
                    0.08f,
                    0.2f);
            }
            else if (key.Contains("forge3dfightermissileimpact"))
            {
                resource = "FighterMissileExplosion";
                lifetime = 1.4f;
                scale = Mathf.Clamp(radius * 0.032f, 0.1f, 0.16f);
            }
            else if (key.Contains("forge3dguidedmissileimpact"))
            {
                resource = "MissileExplosion";
                lifetime = 1.4f;
                scale = Mathf.Clamp(radius * 0.028f, 0.1f, 0.15f);
            }
            else if (key.Contains("forge3dmissileimpact"))
            {
                resource = "MissileExplosion";
                lifetime = 1.35f;
                scale = Mathf.Clamp(radius * 0.03f, 0.1f, 0.15f);
            }

            if (resource == null)
                return false;

            ResolvePool(host).SpawnOneShot(
                ResourceRoot + resource,
                position,
                normal.sqrMagnitude > 0.01f
                    ? normal
                    : Vector3.up,
                lifetime,
                scale);
            return true;
        }

        public static bool TrySpawnTracer(
            Transform host,
            Vector3 start,
            Vector3 end,
            string sourceEffect,
            float lifetime)
        {
            string key = Normalize(sourceEffect);
            Vector3 delta = end - start;
            if (delta.sqrMagnitude < 0.0001f)
                return false;

            Forge3DEffectPool pool = ResolvePool(host);
            Vector3 direction = delta.normalized;
            if (key.Contains("gatlin") || key.Contains("machinegun"))
            {
                pool.SpawnOneShot(
                    ResourceRoot + "VulcanProjectile",
                    start,
                    direction,
                    Mathf.Max(0.12f, lifetime),
                    key.Contains("gatlin") ? 0.24f : 0.18f);
                return true;
            }
            if (key.Contains("energy_cannon"))
                return true;
            if (key.Contains("forge3dfightermissiletrail"))
            {
                pool.SpawnOneShot(
                    ResourceRoot + "MissileFlame",
                    end,
                    direction,
                    0.2f,
                    0.22f);
                pool.SpawnOneShot(
                    ResourceRoot + "MissileSmokeTrail",
                    end,
                    direction,
                    0.42f,
                    0.09f);
                return true;
            }
            if (key.Contains("forge3dguidedmissiletrail"))
            {
                pool.SpawnOneShot(
                    ResourceRoot + "SeekerFlare",
                    end,
                    direction,
                    0.22f,
                    0.18f);
                pool.SpawnOneShot(
                    ResourceRoot + "MissileSmokeTrail",
                    end,
                    direction,
                    0.48f,
                    0.08f);
                return true;
            }
            if (key.Contains("forge3dmissiletrail"))
            {
                pool.SpawnOneShot(
                    ResourceRoot + "MissileFlame",
                    end,
                    direction,
                    0.18f,
                    0.16f);
                pool.SpawnOneShot(
                    ResourceRoot + "MissileSmokeTrail",
                    end,
                    direction,
                    0.5f,
                    0.1f);
                return true;
            }
            if (!key.Contains("sniper"))
                return false;

            pool.SpawnBeam(
                ResourceRoot + "SniperBeam",
                start,
                end,
                Mathf.Max(0.12f, lifetime));
            pool.SpawnOneShot(
                ResourceRoot + "SniperImpact",
                end,
                (start - end).normalized,
                0.65f,
                0.16f);
            return true;
        }

        public static bool TryGetProjectileVisual(
            string sourceEffect,
            out string resource,
            out float scale)
        {
            string key = Normalize(sourceEffect);
            if (key.Contains("energy_cannon"))
            {
                resource = ResourceRoot + "PlasmaProjectile";
                scale = 0.18f;
                return true;
            }

            resource = null;
            scale = 1f;
            return false;
        }

        static Forge3DEffectPool ResolvePool(Transform host)
        {
            Forge3DEffectPool pool =
                host.GetComponent<Forge3DEffectPool>();
            if (pool == null)
                pool = host.gameObject.AddComponent<Forge3DEffectPool>();
            return pool;
        }

        static string Normalize(string value)
        {
            return (value ?? string.Empty).ToLowerInvariant();
        }
    }

    public sealed class Forge3DEffectPool : MonoBehaviour
    {
        sealed class EffectSlot
        {
            public string Resource;
            public GameObject Root;
            public Vector3 BaseScale;
            public ParticleSystem[] Particles;
            public LineRenderer Line;
            public float EndsAt;
        }

        readonly List<EffectSlot> slots = new List<EffectSlot>();
        readonly Dictionary<string, GameObject> prefabs =
            new Dictionary<string, GameObject>(
                StringComparer.OrdinalIgnoreCase);

        public void SpawnOneShot(
            string resource,
            Vector3 position,
            Vector3 forward,
            float lifetime,
            float scale)
        {
            EffectSlot slot = Acquire(resource);
            if (slot == null)
                return;

            WeaponEffectOrientation.ResetForReuse(slot.Root);
            slot.Root.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(
                    forward.sqrMagnitude > 0.01f
                        ? forward.normalized
                        : Vector3.forward));
            slot.Root.transform.localScale =
                slot.BaseScale * Mathf.Max(0.05f, scale);
            slot.Root.SetActive(true);
            if (slot.Line != null)
                slot.Line.enabled = true;
            RestartParticles(slot);
            slot.EndsAt = Time.time + Mathf.Max(0.05f, lifetime);
        }

        public void SpawnBeam(
            string resource,
            Vector3 start,
            Vector3 end,
            float lifetime)
        {
            EffectSlot slot = Acquire(resource);
            if (slot == null || slot.Line == null)
                return;

            WeaponEffectOrientation.ResetForReuse(slot.Root);
            Vector3 delta = end - start;
            if (delta.sqrMagnitude < 0.0001f)
                return;
            slot.Root.transform.SetPositionAndRotation(
                start,
                Quaternion.LookRotation(delta.normalized));
            slot.Root.transform.localScale = slot.BaseScale;
            slot.Root.SetActive(true);
            slot.Line.enabled = true;
            slot.Line.useWorldSpace = true;
            slot.Line.positionCount = 2;
            slot.Line.SetPosition(0, start);
            slot.Line.SetPosition(1, end);
            RestartParticles(slot);
            slot.EndsAt = Time.time + Mathf.Max(0.05f, lifetime);
        }

        void Update()
        {
            foreach (EffectSlot slot in slots)
            {
                if (!slot.Root.activeSelf || Time.time < slot.EndsAt)
                    continue;
                foreach (ParticleSystem particle in slot.Particles)
                    if (particle != null)
                        particle.Stop(
                            true,
                            ParticleSystemStopBehavior
                                .StopEmittingAndClear);
                slot.Root.SetActive(false);
            }
        }

        EffectSlot Acquire(string resource)
        {
            EffectSlot slot = slots.Find(item =>
                string.Equals(
                    item.Resource,
                    resource,
                    StringComparison.OrdinalIgnoreCase) &&
                !item.Root.activeSelf);
            if (slot != null)
                return slot;

            if (!prefabs.TryGetValue(resource, out GameObject prefab))
            {
                prefab = Resources.Load<GameObject>(resource);
                prefabs[resource] = prefab;
            }
            if (prefab == null)
                return null;

            GameObject root = Instantiate(
                prefab,
                CombatTransientRoot.GetOrCreate());
            root.name = "Forge3D_" + prefab.name;
            foreach (MonoBehaviour behaviour in
                     root.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null)
                    behaviour.enabled = false;
            WeaponEffectOrientation.Normalize(
                root,
                WeaponEffectOrientation.Infer(resource));
            slot = new EffectSlot
            {
                Resource = resource,
                Root = root,
                BaseScale = root.transform.localScale,
                Particles =
                    root.GetComponentsInChildren<ParticleSystem>(true),
                Line = root.GetComponentInChildren<LineRenderer>(true)
            };
            root.SetActive(false);
            slots.Add(slot);
            return slot;
        }

        static void RestartParticles(EffectSlot slot)
        {
            foreach (ParticleSystem particle in slot.Particles)
            {
                if (particle == null)
                    continue;
                particle.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Play(true);
            }
        }
    }

    public sealed class Forge3DWeaponAnimator : MonoBehaviour
    {
        readonly List<MeshRenderer> originalRenderers =
            new List<MeshRenderer>();

        Transform visualPivot;
        Transform sampleProxy;
        AnimationClip fireClip;
        AnimationClip loopClip;
        Vector3 recoilAxisLocal = Vector3.forward;
        float recoilDistance = 0.08f;
        float sequenceStarted;
        float firingUntil;
        bool looping;
        bool initialized;

        public static void Trigger(
            Transform weaponRoot,
            Vector3 directionWorld,
            string sourceEffect)
        {
            if (weaponRoot == null)
                return;
            Forge3DWeaponAnimator animator =
                weaponRoot.GetComponent<Forge3DWeaponAnimator>();
            if (animator == null)
                animator =
                    weaponRoot.gameObject
                        .AddComponent<Forge3DWeaponAnimator>();
            animator.PlayFire(directionWorld, sourceEffect);
        }

        void PlayFire(Vector3 directionWorld, string sourceEffect)
        {
            EnsureInitialized();
            if (visualPivot == null)
                return;
            recoilAxisLocal =
                transform.InverseTransformDirection(directionWorld).normalized;
            looping = (sourceEffect ?? string.Empty).Contains("gatlin");
            if (!looping || Time.time >= firingUntil)
                sequenceStarted = Time.time;
            firingUntil = Time.time + (looping ? 0.14f : 0.11f);
        }

        void Update()
        {
            if (!initialized || visualPivot == null)
                return;

            if (Time.time <= firingUntil)
            {
                AnimationClip clip =
                    looping && loopClip != null ? loopClip : fireClip;
                float sample = 1f;
                if (clip != null && sampleProxy != null)
                {
                    float elapsed = Time.time - sequenceStarted;
                    float time = looping
                        ? Mathf.Repeat(elapsed, Mathf.Max(0.01f, clip.length))
                        : Mathf.Min(elapsed, clip.length);
                    sampleProxy.localPosition = Vector3.zero;
                    clip.SampleAnimation(sampleProxy.gameObject, time);
                    sample = Mathf.Clamp01(
                        Mathf.Abs(sampleProxy.localPosition.z) / 0.97f);
                }
                visualPivot.localPosition =
                    -recoilAxisLocal * recoilDistance * sample;
            }
            else
            {
                visualPivot.localPosition = Vector3.Lerp(
                    visualPivot.localPosition,
                    Vector3.zero,
                    1f - Mathf.Exp(-24f * Time.deltaTime));
            }
        }

        void EnsureInitialized()
        {
            if (initialized)
                return;
            initialized = true;
            fireClip = Resources.Load<AnimationClip>(
                "WeaponEffects/Forge3D/TurretBarrelFire");
            loopClip = Resources.Load<AnimationClip>(
                "WeaponEffects/Forge3D/TurretBarrelLoop");

            GameObject pivotObject =
                new GameObject("Forge3D_RecoilVisual");
            visualPivot = pivotObject.transform;
            visualPivot.SetParent(transform, false);

            GameObject proxyObject =
                new GameObject("Forge3D_AnimationSample");
            sampleProxy = proxyObject.transform;
            sampleProxy.SetParent(transform, false);
            proxyObject.hideFlags = HideFlags.HideInHierarchy;

            Bounds combined = new Bounds(transform.position, Vector3.zero);
            bool hasBounds = false;
            MeshRenderer[] renderers =
                GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer source in renderers)
            {
                if (!ShouldClone(source))
                    continue;
                MeshFilter sourceFilter =
                    source.GetComponent<MeshFilter>();
                if (sourceFilter == null ||
                    sourceFilter.sharedMesh == null)
                    continue;

                GameObject cloneObject =
                    new GameObject(source.gameObject.name + "_Animated");
                cloneObject.layer = source.gameObject.layer;
                Transform clone = cloneObject.transform;
                clone.SetParent(visualPivot, false);
                CopyRelativeTransform(
                    transform,
                    source.transform,
                    clone);

                MeshFilter cloneFilter =
                    cloneObject.AddComponent<MeshFilter>();
                cloneFilter.sharedMesh = sourceFilter.sharedMesh;
                MeshRenderer cloneRenderer =
                    cloneObject.AddComponent<MeshRenderer>();
                cloneRenderer.sharedMaterials = source.sharedMaterials;
                cloneRenderer.shadowCastingMode =
                    source.shadowCastingMode;
                cloneRenderer.receiveShadows = source.receiveShadows;
                cloneRenderer.lightProbeUsage = source.lightProbeUsage;
                cloneRenderer.reflectionProbeUsage =
                    source.reflectionProbeUsage;
                MaterialPropertyBlock properties =
                    new MaterialPropertyBlock();
                source.GetPropertyBlock(properties);
                cloneRenderer.SetPropertyBlock(properties);

                originalRenderers.Add(source);
                source.enabled = false;
                if (!hasBounds)
                {
                    combined = source.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(source.bounds);
                }
            }

            if (originalRenderers.Count == 0)
            {
                Destroy(pivotObject);
                visualPivot = null;
                return;
            }
            recoilDistance = Mathf.Clamp(
                hasBounds ? combined.size.magnitude * 0.025f : 0.08f,
                0.04f,
                0.24f);
        }

        static bool ShouldClone(MeshRenderer renderer)
        {
            if (renderer == null || !renderer.enabled)
                return false;
            string name = renderer.gameObject.name.ToLowerInvariant();
            return !name.Contains("proxy") &&
                   !name.Contains("ghost") &&
                   !name.Contains("collider") &&
                   !name.StartsWith("forge3d_");
        }

        static void CopyRelativeTransform(
            Transform root,
            Transform source,
            Transform destination)
        {
            Matrix4x4 matrix =
                root.worldToLocalMatrix * source.localToWorldMatrix;
            Vector3 right = matrix.GetColumn(0);
            Vector3 up = matrix.GetColumn(1);
            Vector3 forward = matrix.GetColumn(2);
            Vector3 scale = new Vector3(
                right.magnitude,
                up.magnitude,
                forward.magnitude);
            if (Vector3.Dot(Vector3.Cross(right, up), forward) < 0f)
                scale.x = -scale.x;
            destination.localPosition = matrix.GetColumn(3);
            destination.localRotation = Quaternion.LookRotation(
                forward.normalized,
                up.normalized);
            destination.localScale = scale;
        }

        void OnDestroy()
        {
            foreach (MeshRenderer renderer in originalRenderers)
                if (renderer != null)
                    renderer.enabled = true;
        }
    }
}
