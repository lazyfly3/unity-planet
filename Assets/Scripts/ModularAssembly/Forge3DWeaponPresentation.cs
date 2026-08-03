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

            bool spawned = ResolvePool(host).SpawnOneShot(
                ResourceRoot + resource,
                position,
                direction,
                lifetime,
                scale);
            if (!spawned)
                return false;

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

            return ResolvePool(host).SpawnOneShot(
                ResourceRoot + resource,
                position,
                normal.sqrMagnitude > 0.01f
                    ? normal
                    : Vector3.up,
                lifetime,
                scale);
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

            if (key.Contains("gatlin") || key.Contains("machinegun"))
                // VulcanProjectile is a static mesh. Returning true here used
                // to suppress the real start-to-end LineRenderer tracer.
                return false;

            if (key.Contains("energy_cannon"))
                // The physical energy projectile always owns a visible body
                // fallback, so no Forge pool allocation is required here.
                return true;

            Forge3DEffectPool pool = ResolvePool(host);
            Vector3 direction = delta.normalized;
            if (key.Contains("forge3dfightermissiletrail"))
            {
                bool spawned = pool.SpawnOneShot(
                    ResourceRoot + "MissileFlame",
                    end,
                    direction,
                    0.2f,
                    0.22f);
                spawned |= pool.SpawnOneShot(
                    ResourceRoot + "MissileSmokeTrail",
                    end,
                    direction,
                    0.42f,
                    0.09f);
                return spawned;
            }
            if (key.Contains("forge3dguidedmissiletrail"))
            {
                bool spawned = pool.SpawnOneShot(
                    ResourceRoot + "SeekerFlare",
                    end,
                    direction,
                    0.22f,
                    0.18f);
                spawned |= pool.SpawnOneShot(
                    ResourceRoot + "MissileSmokeTrail",
                    end,
                    direction,
                    0.48f,
                    0.08f);
                return spawned;
            }
            if (key.Contains("forge3dmissiletrail"))
            {
                bool spawned = pool.SpawnOneShot(
                    ResourceRoot + "MissileFlame",
                    end,
                    direction,
                    0.18f,
                    0.16f);
                spawned |= pool.SpawnOneShot(
                    ResourceRoot + "MissileSmokeTrail",
                    end,
                    direction,
                    0.5f,
                    0.1f);
                return spawned;
            }
            if (!key.Contains("sniper"))
                return false;

            return pool.SpawnBeam(
                ResourceRoot + "SniperBeam",
                start,
                end,
                Mathf.Max(0.12f, lifetime));
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
            Transform transientRoot = CombatTransientRoot.GetOrCreate();
            Forge3DEffectPool pool =
                transientRoot.GetComponent<Forge3DEffectPool>();
            if (pool == null)
                pool = transientRoot.gameObject
                    .AddComponent<Forge3DEffectPool>();
            return pool;
        }

        static string Normalize(string value)
        {
            return (value ?? string.Empty).ToLowerInvariant();
        }
    }

    public sealed class Forge3DEffectPool : MonoBehaviour
    {
        public const int MaximumSlots = 64;

        sealed class EffectSlot
        {
            public string Resource;
            public GameObject Root;
            public Vector3 BaseScale;
            public ParticleSystem[] Particles;
            public Renderer[] Renderers;
            public LineRenderer Line;
            public float EndsAt;
        }

        readonly List<EffectSlot> slots = new List<EffectSlot>();
        readonly Dictionary<string, GameObject> prefabs =
            new Dictionary<string, GameObject>(
                StringComparer.OrdinalIgnoreCase);
        public int SlotCount => slots.Count;

        public bool SpawnOneShot(
            string resource,
            Vector3 position,
            Vector3 forward,
            float lifetime,
            float scale)
        {
            EffectSlot slot = Acquire(resource);
            if (slot == null)
                return false;

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
            if (!HasRenderableRenderer(slot))
            {
                Reject(slot);
                return false;
            }
            slot.EndsAt = Time.time + Mathf.Max(0.05f, lifetime);
            return true;
        }

        public bool SpawnBeam(
            string resource,
            Vector3 start,
            Vector3 end,
            float lifetime)
        {
            Vector3 delta = end - start;
            if (delta.sqrMagnitude < 0.0001f)
                return false;

            EffectSlot slot = Acquire(resource);
            if (slot == null || slot.Line == null)
                return false;

            WeaponEffectOrientation.ResetForReuse(slot.Root);
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
            if (!slot.Line.enabled ||
                !slot.Line.gameObject.activeInHierarchy ||
                !RendererIsUsable(slot.Line) ||
                !HasRenderableRenderer(slot))
            {
                Reject(slot);
                return false;
            }
            slot.EndsAt = Time.time + Mathf.Max(0.05f, lifetime);
            return true;
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

        public void Clear()
        {
            foreach (EffectSlot slot in slots)
                Reject(slot);
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

            if (slots.Count >= MaximumSlots)
            {
                foreach (EffectSlot candidate in slots)
                {
                    if (!string.Equals(
                            candidate.Resource,
                            resource,
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (slot == null ||
                        candidate.EndsAt < slot.EndsAt)
                        slot = candidate;
                }
                if (slot == null)
                    return null;
                Reject(slot);
                return slot;
            }

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
                Renderers =
                    root.GetComponentsInChildren<Renderer>(true),
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

        static bool HasRenderableRenderer(EffectSlot slot)
        {
            return slot != null &&
                   HasRenderableRendererSet(slot.Renderers);
        }

        public static bool HasRenderableRendererSet(GameObject root)
        {
            return root != null &&
                   HasRenderableRendererSet(
                       root.GetComponentsInChildren<Renderer>(true));
        }

        static bool HasRenderableRendererSet(Renderer[] renderers)
        {
            if (renderers == null)
                return false;
            bool found = false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null ||
                    !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy)
                    continue;
                found = true;
                if (!RendererIsUsable(renderer))
                    return false;
            }
            return found;
        }

        static bool RendererIsUsable(Renderer renderer)
        {
            if (renderer == null)
                return false;
            if (renderer is ParticleSystemRenderer particleRenderer)
            {
                if (!MaterialIsUsable(particleRenderer.sharedMaterial))
                    return false;
                ParticleSystem particle =
                    particleRenderer.GetComponent<ParticleSystem>();
                return particle == null ||
                       !particle.trails.enabled ||
                       MaterialIsUsable(particleRenderer.trailMaterial);
            }

            Material[] values = renderer.sharedMaterials;
            if (values == null || values.Length == 0)
                return false;
            foreach (Material material in values)
            {
                if (!MaterialIsUsable(material))
                    return false;
            }
            return true;
        }

        static bool MaterialIsUsable(Material material)
        {
            return material != null &&
                   material.shader != null &&
                   material.shader.isSupported &&
                   material.shader.name.IndexOf(
                       "InternalErrorShader",
                       StringComparison.OrdinalIgnoreCase) < 0;
        }

        static void Reject(EffectSlot slot)
        {
            if (slot == null)
                return;
            foreach (ParticleSystem particle in slot.Particles)
            {
                if (particle != null)
                {
                    particle.Stop(
                        true,
                        ParticleSystemStopBehavior.StopEmittingAndClear);
                    particle.Clear(true);
                }
            }
            if (slot.Line != null)
                slot.Line.enabled = false;
            if (slot.Root != null)
                slot.Root.SetActive(false);
            slot.EndsAt = 0f;
        }

        void OnDestroy()
        {
            foreach (EffectSlot slot in slots)
            {
                if (slot?.Root == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(slot.Root);
                else
                    DestroyImmediate(slot.Root);
            }
            slots.Clear();
            prefabs.Clear();
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
                Destroy(proxyObject);
                visualPivot = null;
                sampleProxy = null;
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
            if (renderer.GetComponentInParent<LODGroup>() != null)
                return false;
            string name = renderer.gameObject.name.ToLowerInvariant();
            if (name.Contains("proxy") ||
                name.Contains("ghost") ||
                name.Contains("collider") ||
                name.Contains("collision") ||
                name.StartsWith("forge3d_"))
                return false;
            return name.Contains("barrel") ||
                   name.Contains("muzzle") ||
                   name.Contains("bolt") ||
                   name.Contains("slide") ||
                   name.Contains("recoil");
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
