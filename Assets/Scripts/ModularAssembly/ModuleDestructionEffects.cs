using System;
using System.Collections.Generic;
using ModularAssembly;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    // Visual feedback only. Damage and topology remain authoritative in
    // VehicleStructureGraph.
    public enum ModuleDestructionEffectTier
    {
        Structural,
        Functional,
        Core
    }

    public readonly struct ModuleDestructionFeedbackContext
    {
        public readonly string RuntimeId;
        public readonly Vector3 HitPoint;
        public readonly Vector3 HitNormal;
        public readonly GridModuleCategory Category;
        public readonly bool IsCore;
        public readonly Bounds WorldBounds;
        public readonly int DetachedComponentCount;
        public readonly float VisualScale;

        public ModuleDestructionFeedbackContext(
            string runtimeId,
            Vector3 hitPoint,
            Vector3 hitNormal,
            GridModuleCategory category,
            bool isCore,
            Bounds worldBounds,
            int detachedComponentCount,
            float visualScale = 1f)
        {
            RuntimeId = runtimeId ?? string.Empty;
            HitPoint = hitPoint;
            HitNormal = hitNormal.sqrMagnitude > 0.0001f
                ? hitNormal.normalized
                : Vector3.up;
            Category = category;
            IsCore = isCore;
            WorldBounds = worldBounds;
            DetachedComponentCount =
                Mathf.Max(0, detachedComponentCount);
            VisualScale = Mathf.Max(0.05f, visualScale);
        }
    }

    public sealed class ModuleDestructionEffectDefinition
    {
        public readonly ModuleDestructionEffectTier Tier;
        public readonly string PrimaryResource;
        public readonly string FallbackResource;
        public readonly float PrimarySourceRadius;
        public readonly float FallbackSourceRadius;
        public readonly float TargetRadius;
        public readonly float Lifetime;
        public readonly float SmokeLifetime;
        public readonly int MaximumConcurrent;

        public ModuleDestructionEffectDefinition(
            ModuleDestructionEffectTier tier,
            string primaryResource,
            string fallbackResource,
            float primarySourceRadius,
            float fallbackSourceRadius,
            float targetRadius,
            float lifetime,
            float smokeLifetime,
            int maximumConcurrent)
        {
            Tier = tier;
            PrimaryResource = primaryResource;
            FallbackResource = fallbackResource;
            PrimarySourceRadius = Mathf.Max(0.01f, primarySourceRadius);
            FallbackSourceRadius = Mathf.Max(0.01f, fallbackSourceRadius);
            TargetRadius = Mathf.Max(0.05f, targetRadius);
            Lifetime = Mathf.Max(0.1f, lifetime);
            SmokeLifetime = Mathf.Clamp(smokeLifetime, 0.05f, Lifetime);
            MaximumConcurrent = Mathf.Max(1, maximumConcurrent);
        }

        public GameObject LoadPrefab(out float sourceRadius)
        {
            GameObject prefab =
                Resources.Load<GameObject>(PrimaryResource);
            sourceRadius = PrimarySourceRadius;
            if (prefab != null)
                return prefab;
            prefab = Resources.Load<GameObject>(FallbackResource);
            sourceRadius = FallbackSourceRadius;
            return prefab;
        }

        public float ResolveVisualTargetRadius(
            Bounds worldBounds,
            int detachedComponentCount)
        {
            float minimum = TargetRadius * 0.75f;
            float maximum = TargetRadius *
                            (Tier == ModuleDestructionEffectTier.Core
                                ? 1.65f
                                : 1.5f);
            Vector3 size = worldBounds.size;
            float maximumSize = Mathf.Max(
                Mathf.Abs(size.x),
                Mathf.Abs(size.y),
                Mathf.Abs(size.z));
            float desired = maximumSize > 0.001f &&
                            !float.IsNaN(maximumSize) &&
                            !float.IsInfinity(maximumSize)
                ? maximumSize * 0.65f
                : TargetRadius;
            float detachedBoost = 1f + Mathf.Clamp(
                Mathf.Sqrt(Mathf.Max(0, detachedComponentCount)) * 0.04f,
                0f,
                0.2f);
            return Mathf.Clamp(
                desired * detachedBoost,
                minimum,
                maximum);
        }
    }

    public sealed class ModuleDestructionEffectCatalog
    {
        const string Root =
            "CombatFeedback/HQExplosions/" +
            "Realistic explosions/Prefabs/";

        readonly Dictionary<
            ModuleDestructionEffectTier,
            ModuleDestructionEffectDefinition> definitions =
            new Dictionary<
                ModuleDestructionEffectTier,
                ModuleDestructionEffectDefinition>
            {
                {
                    ModuleDestructionEffectTier.Structural,
                    new ModuleDestructionEffectDefinition(
                        ModuleDestructionEffectTier.Structural,
                        Root + "Explosion14",
                        Root + "Explosion7",
                        2f,
                        2.5f,
                        0.78f,
                        0.5f,
                        0.25f,
                        16)
                },
                {
                    ModuleDestructionEffectTier.Functional,
                    new ModuleDestructionEffectDefinition(
                        ModuleDestructionEffectTier.Functional,
                        Root + "Explosion10",
                        Root + "Explosion21",
                        5f,
                        5f,
                        1.5f,
                        1.05f,
                        0.25f,
                        8)
                },
                {
                    ModuleDestructionEffectTier.Core,
                    new ModuleDestructionEffectDefinition(
                        ModuleDestructionEffectTier.Core,
                        Root + "Explosion25",
                        Root + "Explosion20",
                        6f,
                        5.5f,
                        3.7f,
                        1.45f,
                        0.25f,
                        2)
                }
            };

        public ModuleDestructionEffectDefinition Resolve(
            GridModuleCategory category,
            bool isCore)
        {
            return definitions[ResolveTier(category, isCore)];
        }

        public ModuleDestructionEffectDefinition Resolve(
            ModuleDestructionEffectTier tier)
        {
            return definitions[tier];
        }

        public static ModuleDestructionEffectTier ResolveTier(
            GridModuleCategory category,
            bool isCore)
        {
            if (isCore || category == GridModuleCategory.Core)
                return ModuleDestructionEffectTier.Core;
            switch (category)
            {
                case GridModuleCategory.MainThruster:
                case GridModuleCategory.RcsThruster:
                case GridModuleCategory.KineticWeapon:
                case GridModuleCategory.Battery:
                case GridModuleCategory.Mobility:
                    return ModuleDestructionEffectTier.Functional;
                default:
                    return ModuleDestructionEffectTier.Structural;
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class CombatFeedbackController : MonoBehaviour
    {
        sealed class GraphSubscription
        {
            public Action<VehicleModuleDamageFeedback> DamageHandler;
            public Action<VehicleStructureDelta> StructureHandler;
            public Action DestroyedHandler;
        }

        const string VehicleDestroyedVisualId =
            "$vehicle-destroyed";

        static CombatFeedbackController instance;

        readonly Dictionary<VehicleStructureGraph, GraphSubscription>
            subscriptions =
                new Dictionary<
                    VehicleStructureGraph,
                    GraphSubscription>();
        readonly Dictionary<VehicleStructureGraph, HashSet<string>>
            playedRuntimeIds =
                new Dictionary<VehicleStructureGraph, HashSet<string>>();

        ModuleDestructionEffectPool effectPool;

        public static CombatFeedbackController GetOrCreate()
        {
            if (instance != null)
                return instance;
            Transform transientRoot = CombatTransientRoot.GetOrCreate();
            instance =
                transientRoot.GetComponent<CombatFeedbackController>() ??
                transientRoot.gameObject
                    .AddComponent<CombatFeedbackController>();
            instance.EnsurePool();
            return instance;
        }

        void Awake()
        {
            if (instance == null)
                instance = this;
            EnsurePool();
        }

        void EnsurePool()
        {
            if (effectPool == null)
            {
                effectPool =
                    GetComponent<ModuleDestructionEffectPool>() ??
                    gameObject.AddComponent<
                        ModuleDestructionEffectPool>();
            }
            effectPool.Prewarm();
        }

        public void Observe(VehicleStructureGraph graph)
        {
            if (graph == null)
                return;
            if (subscriptions.ContainsKey(graph))
            {
                if (playedRuntimeIds.TryGetValue(
                        graph,
                        out HashSet<string> played))
                    played.Clear();
                return;
            }
            var subscription = new GraphSubscription();
            subscription.DamageHandler =
                feedback => HandleModuleDamaged(graph, feedback);
            subscription.StructureHandler =
                delta => HandleStructureChanged(graph, delta);
            subscription.DestroyedHandler =
                () => HandleVehicleDestroyed(graph);
            graph.ModuleDamaged += subscription.DamageHandler;
            graph.StructureChanged += subscription.StructureHandler;
            graph.Destroyed += subscription.DestroyedHandler;
            subscriptions.Add(graph, subscription);
            playedRuntimeIds[graph] =
                new HashSet<string>(StringComparer.Ordinal);
        }

        public bool PlayDestruction(
            ModuleDestructionFeedbackContext context)
        {
            EnsurePool();
            CombatTransientRoot.EnsureCameraDepthTexture();
            return effectPool.Spawn(context);
        }

        void HandleModuleDamaged(
            VehicleStructureGraph graph,
            VehicleModuleDamageFeedback feedback)
        {
            if (!feedback.Destroyed)
                return;
            if (!playedRuntimeIds.TryGetValue(
                    graph,
                    out HashSet<string> played))
            {
                played = new HashSet<string>(StringComparer.Ordinal);
                playedRuntimeIds[graph] = played;
            }
            if (played.Contains(feedback.RuntimeId))
                return;
            Vector3 normal = feedback.Impulse.sqrMagnitude > 0.0001f
                ? -feedback.Impulse.normalized
                : Vector3.up;
            Bounds visualBounds = feedback.WorldBounds;
            if (feedback.IsCore)
            {
                Bounds vehicleBounds = graph.ResolveVisualBounds();
                if (HasVisualExtent(vehicleBounds))
                    visualBounds = vehicleBounds;
            }
            bool spawned = PlayDestruction(
                new ModuleDestructionFeedbackContext(
                    feedback.RuntimeId,
                    feedback.HitPoint,
                    normal,
                    feedback.Category,
                    feedback.IsCore,
                    visualBounds,
                    0));
            if (spawned)
                played.Add(feedback.RuntimeId);
        }

        void HandleVehicleDestroyed(VehicleStructureGraph graph)
        {
            if (graph == null)
                return;
            if (!playedRuntimeIds.TryGetValue(
                    graph,
                    out HashSet<string> played))
            {
                played = new HashSet<string>(StringComparer.Ordinal);
                playedRuntimeIds[graph] = played;
            }

            // A direct core hit already emitted a core-tier effect from the
            // ModuleDamaged callback. The Destroyed callback must only add
            // the vehicle-scale cue for connectivity/CPU collapse.
            if (played.Contains(GridAssemblyModel.CoreRuntimeId) ||
                played.Contains(VehicleDestroyedVisualId))
                return;

            VehicleStructureDelta delta = graph.LastDestructionDelta;
            Bounds bounds = graph.ResolveVisualBounds();
            bool hasBounds = HasVisualExtent(bounds);
            Vector3 point = hasBounds
                ? bounds.center
                : delta != null
                    ? delta.HitPoint
                    : graph.transform.position;
            Vector3 impulse =
                delta != null ? delta.Impulse : Vector3.zero;
            Vector3 normal = impulse.sqrMagnitude > 0.0001f
                ? -impulse.normalized
                : Vector3.up;
            int detachedCount =
                delta?.DetachedComponents != null
                    ? delta.DetachedComponents.Count
                    : 0;

            bool spawned = PlayDestruction(
                new ModuleDestructionFeedbackContext(
                    VehicleDestroyedVisualId,
                    point,
                    normal,
                    GridModuleCategory.Core,
                    true,
                    hasBounds
                        ? bounds
                        : new Bounds(point, Vector3.one),
                    detachedCount));
            if (spawned)
                played.Add(VehicleDestroyedVisualId);
        }

        void HandleStructureChanged(
            VehicleStructureGraph graph,
            VehicleStructureDelta delta)
        {
            if (delta == null)
                return;
            if (string.IsNullOrEmpty(delta.DirectHitRuntimeId))
            {
                if (playedRuntimeIds.TryGetValue(
                        graph,
                        out HashSet<string> played))
                    played.Clear();
                return;
            }
            int detachedCount =
                delta.DetachedComponents != null
                    ? delta.DetachedComponents.Count
                    : 0;
            if (detachedCount <= 0)
                return;
            Vector3 normal = delta.Impulse.sqrMagnitude > 0.0001f
                ? -delta.Impulse.normalized
                : Vector3.up;
            EnsurePool();
            effectPool.SpawnSeverFlash(
                delta.HitPoint,
                normal,
                ResolveObserverDistance(delta.HitPoint));
        }

        static float ResolveObserverDistance(Vector3 point)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                Camera[] cameras = Camera.allCameras;
                if (cameras.Length > 0)
                    camera = cameras[0];
            }
            return camera != null
                ? Vector3.Distance(camera.transform.position, point)
                : 0f;
        }

        static bool HasVisualExtent(Bounds bounds)
        {
            Vector3 size = bounds.size;
            return IsFinite(size.x) &&
                   IsFinite(size.y) &&
                   IsFinite(size.z) &&
                   size.sqrMagnitude > 0.0001f;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        void OnDestroy()
        {
            foreach (KeyValuePair<
                         VehicleStructureGraph,
                         GraphSubscription> pair in subscriptions)
            {
                if (pair.Key == null)
                    continue;
                pair.Key.ModuleDamaged -= pair.Value.DamageHandler;
                pair.Key.StructureChanged -=
                    pair.Value.StructureHandler;
                pair.Key.Destroyed -= pair.Value.DestroyedHandler;
            }
            subscriptions.Clear();
            playedRuntimeIds.Clear();
            if (instance == this)
                instance = null;
        }
    }

    [DisallowMultipleComponent]
    sealed class ModuleDestructionEffectPool : MonoBehaviour
    {
        sealed class RendererState
        {
            public Renderer Renderer;
            public bool InitiallyEnabled;
            public bool Invalid;
            public bool Minor;
        }

        sealed class ParticleState
        {
            public ParticleSystem Particle;
            public float BaseLifetimeMultiplier;
            public float BaseLifetimeMaximum;
            public int BaseMaximumParticles;
            public bool Smoke;
        }

        sealed class EffectSlot
        {
            public ModuleDestructionEffectTier Tier;
            public GameObject Root;
            public Vector3 BaseScale;
            public float SourceRadius;
            public RendererState[] Renderers;
            public ParticleState[] Particles;
            public Light[] Lights;
            public float EndsAt;
            public float LightEndsAt;
            public bool Active;
        }

        sealed class SeverFlashSlot
        {
            public LineRenderer Line;
            public Vector3 Center;
            public Vector3 Normal;
            public float StartedAt;
            public float EndsAt;
            public bool Active;
        }

        readonly ModuleDestructionEffectCatalog catalog =
            new ModuleDestructionEffectCatalog();
        readonly List<EffectSlot> slots =
            new List<EffectSlot>();
        readonly List<SeverFlashSlot> severFlashes =
            new List<SeverFlashSlot>();
        readonly HashSet<ModuleDestructionEffectTier>
            unavailableTiers =
                new HashSet<ModuleDestructionEffectTier>();
        readonly HashSet<ModuleDestructionEffectTier>
            loggedUnavailableTiers =
                new HashSet<ModuleDestructionEffectTier>();

        Material severMaterial;
        bool prewarmed;
        bool loggedSeverUnavailable;

        public void Prewarm()
        {
            if (prewarmed)
                return;
            prewarmed = true;
            PrewarmTier(ModuleDestructionEffectTier.Structural, 4);
            PrewarmTier(ModuleDestructionEffectTier.Functional, 2);
            PrewarmTier(ModuleDestructionEffectTier.Core, 1);
        }

        public void Clear()
        {
            foreach (EffectSlot slot in slots)
                StopSlot(slot);
            foreach (SeverFlashSlot flash in severFlashes)
            {
                if (flash == null)
                    continue;
                if (flash.Line != null)
                    flash.Line.enabled = false;
                flash.Active = false;
                flash.StartedAt = 0f;
                flash.EndsAt = 0f;
            }
        }

        public void ShiftWorld(Vector3 delta)
        {
            if (delta.sqrMagnitude < 0.000001f)
                return;
            foreach (SeverFlashSlot flash in severFlashes)
            {
                if (flash != null)
                    flash.Center += delta;
            }
        }

        void PrewarmTier(
            ModuleDestructionEffectTier tier,
            int count)
        {
            for (int index = 0; index < count; index++)
                CreateSlot(catalog.Resolve(tier));
        }

        public bool Spawn(ModuleDestructionFeedbackContext context)
        {
            PruneDestroyedSlots();
            ModuleDestructionEffectDefinition definition =
                catalog.Resolve(context.Category, context.IsCore);
            int activeCount = 0;
            EffectSlot oldest = null;
            EffectSlot available = null;
            foreach (EffectSlot slot in slots)
            {
                if (slot.Tier != definition.Tier)
                    continue;
                if (!slot.Active && available == null)
                    available = slot;
                if (!slot.Active)
                    continue;
                activeCount++;
                if (oldest == null ||
                    slot.EndsAt < oldest.EndsAt)
                    oldest = slot;
            }
            if (activeCount >= definition.MaximumConcurrent)
            {
                if (definition.Tier ==
                    ModuleDestructionEffectTier.Structural)
                    return false;
                available = oldest;
                StopSlot(available);
            }
            if (available == null)
                available = CreateSlot(definition);
            if (available == null)
                return false;

            ActivateSlot(
                available,
                definition,
                context,
                ResolveObserverDistance(context.HitPoint));
            return true;
        }

        public void SpawnSeverFlash(
            Vector3 point,
            Vector3 normal,
            float observerDistance)
        {
            if (observerDistance > 110f)
                return;
            SeverFlashSlot slot = null;
            foreach (SeverFlashSlot candidate in severFlashes)
            {
                if (!candidate.Active)
                {
                    slot = candidate;
                    break;
                }
            }
            if (slot == null && severFlashes.Count < 8)
                slot = CreateSeverFlash();
            if (slot == null && severFlashes.Count > 0)
            {
                slot = severFlashes[0];
                foreach (SeverFlashSlot candidate in severFlashes)
                    if (candidate.EndsAt < slot.EndsAt)
                        slot = candidate;
            }
            if (slot == null)
                return;
            slot.Center = point;
            slot.Normal = normal.sqrMagnitude > 0.0001f
                ? normal.normalized
                : Vector3.up;
            slot.StartedAt = Time.time;
            slot.EndsAt = Time.time + 0.22f;
            slot.Active = true;
            slot.Line.enabled = true;
            UpdateSeverFlash(slot);
        }

        EffectSlot CreateSlot(
            ModuleDestructionEffectDefinition definition)
        {
            if (unavailableTiers.Contains(definition.Tier))
                return null;

            bool primaryReady = TryCreateUsableRoot(
                definition.PrimaryResource,
                out GameObject root,
                out RendererState[] rendererStates,
                out string primaryFailure);
            float sourceRadius = definition.PrimarySourceRadius;
            string fallbackFailure = string.Empty;
            if (!primaryReady)
            {
                bool fallbackReady = TryCreateUsableRoot(
                    definition.FallbackResource,
                    out root,
                    out rendererStates,
                    out fallbackFailure);
                sourceRadius = definition.FallbackSourceRadius;
                if (!fallbackReady)
                {
                    unavailableTiers.Add(definition.Tier);
                    if (loggedUnavailableTiers.Add(definition.Tier))
                    {
                        Debug.LogError(
                            "[Combat VFX] No usable " +
                            definition.Tier +
                            " module-destruction prefab. Primary '" +
                            definition.PrimaryResource +
                            "': " + primaryFailure +
                            "; fallback '" +
                            definition.FallbackResource +
                            "': " + fallbackFailure +
                            ". Spawn was rejected so an invisible or " +
                            "unsupported effect cannot be reported as " +
                            "successful.",
                            this);
                    }
                    return null;
                }
            }

            root.name =
                "PooledModuleExplosion_" + definition.Tier;
            foreach (MonoBehaviour behaviour in
                     root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null)
                    behaviour.enabled = false;
            }
            WeaponEffectOrientation.Normalize(
                root,
                WeaponEffectRole.Explosion);

            ParticleSystem[] particles =
                root.GetComponentsInChildren<ParticleSystem>(true);
            var particleStates =
                new ParticleState[particles.Length];
            for (int index = 0; index < particles.Length; index++)
            {
                ParticleSystem particle = particles[index];
                ParticleSystem.MainModule main = particle.main;
                particleStates[index] = new ParticleState
                {
                    Particle = particle,
                    BaseLifetimeMultiplier =
                        main.startLifetimeMultiplier,
                    BaseLifetimeMaximum =
                        main.startLifetime.constantMax *
                        main.startLifetimeMultiplier,
                    BaseMaximumParticles = main.maxParticles,
                    Smoke = IsSmoke(particle.name)
                };
                main.playOnAwake = false;
                main.loop = false;
                main.simulationSpace =
                    ParticleSystemSimulationSpace.World;
                main.scalingMode =
                    ParticleSystemScalingMode.Hierarchy;
                particle.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            var slot = new EffectSlot
            {
                Tier = definition.Tier,
                Root = root,
                BaseScale = root.transform.localScale,
                SourceRadius = Mathf.Max(0.01f, sourceRadius),
                Renderers = rendererStates,
                Particles = particleStates,
                Lights = root.GetComponentsInChildren<Light>(true)
            };
            root.SetActive(false);
            slots.Add(slot);
            return slot;
        }

        bool TryCreateUsableRoot(
            string resourcePath,
            out GameObject root,
            out RendererState[] rendererStates,
            out string failure)
        {
            root = null;
            rendererStates = Array.Empty<RendererState>();
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                failure = "resource path is empty";
                return false;
            }

            GameObject prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                failure = "resource was not found";
                return false;
            }

            root = Instantiate(prefab, transform, false);
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            rendererStates = new RendererState[renderers.Length];
            bool hasUsableInitiallyEnabledRenderer = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                bool invalid = InvalidRenderer(renderer);
                rendererStates[index] = new RendererState
                {
                    Renderer = renderer,
                    InitiallyEnabled = renderer.enabled,
                    Invalid = invalid,
                    Minor = IsMinor(renderer.name)
                };
                hasUsableInitiallyEnabledRenderer |=
                    renderer.enabled && !invalid;
            }

            if (hasUsableInitiallyEnabledRenderer)
            {
                failure = string.Empty;
                return true;
            }

            failure = renderers.Length == 0
                ? "prefab contains no Renderer"
                : "prefab has no initially-enabled Renderer with a " +
                  "supported, valid material/shader";
            root.SetActive(false);
            if (Application.isPlaying)
                Destroy(root);
            else
                DestroyImmediate(root);
            root = null;
            rendererStates = Array.Empty<RendererState>();
            return false;
        }

        void ActivateSlot(
            EffectSlot slot,
            ModuleDestructionEffectDefinition definition,
            ModuleDestructionFeedbackContext context,
            float observerDistance)
        {
            StopSlot(slot);
            slot.Root.transform.SetPositionAndRotation(
                context.HitPoint,
                Quaternion.FromToRotation(
                    Vector3.up,
                    context.HitNormal));
            float targetRadius =
                definition.ResolveVisualTargetRadius(
                    context.WorldBounds,
                    context.DetachedComponentCount) *
                context.VisualScale;
            float scale = targetRadius / slot.SourceRadius;
            slot.Root.transform.localScale = slot.BaseScale * scale;
            ConfigureRenderers(slot, observerDistance);
            ConfigureParticles(
                slot,
                definition,
                observerDistance);
            ConfigureLights(slot, definition);
            slot.Root.SetActive(true);
            foreach (ParticleState state in slot.Particles)
            {
                if (state.Particle == null)
                    continue;
                state.Particle.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
                state.Particle.Play(true);
            }
            slot.EndsAt = Time.time + definition.Lifetime;
            slot.LightEndsAt =
                definition.Tier == ModuleDestructionEffectTier.Core
                    ? Time.time + 0.1f
                    : 0f;
            slot.Active = true;
        }

        void ConfigureRenderers(
            EffectSlot slot,
            float observerDistance)
        {
            RendererState dominant = null;
            float dominantSize = float.MinValue;
            foreach (RendererState state in slot.Renderers)
            {
                if (state.Renderer == null ||
                    state.Invalid ||
                    !state.InitiallyEnabled)
                    continue;
                float size = state.Renderer.bounds.size.sqrMagnitude;
                if (size > dominantSize)
                {
                    dominant = state;
                    dominantSize = size;
                }
            }
            bool hasEnabledRenderer = false;
            foreach (RendererState state in slot.Renderers)
            {
                if (state.Renderer == null)
                    continue;
                bool enabled =
                    state.InitiallyEnabled && !state.Invalid;
                if (observerDistance > 80f)
                    enabled &= state == dominant;
                else if (observerDistance > 30f)
                    enabled &= !state.Minor;
                state.Renderer.enabled = enabled;
                hasEnabledRenderer |= enabled;
            }
            if (!hasEnabledRenderer &&
                dominant?.Renderer != null)
                dominant.Renderer.enabled = true;
        }

        void ConfigureParticles(
            EffectSlot slot,
            ModuleDestructionEffectDefinition definition,
            float observerDistance)
        {
            foreach (ParticleState state in slot.Particles)
            {
                if (state.Particle == null)
                    continue;
                ParticleSystem.MainModule main =
                    state.Particle.main;
                float multiplier = state.BaseLifetimeMultiplier;
                if (state.Smoke &&
                    state.BaseLifetimeMaximum >
                    definition.SmokeLifetime)
                {
                    multiplier *=
                        definition.SmokeLifetime /
                        Mathf.Max(
                            0.01f,
                            state.BaseLifetimeMaximum);
                }
                main.startLifetimeMultiplier = multiplier;
                main.maxParticles = observerDistance > 80f
                    ? Mathf.Min(12, state.BaseMaximumParticles)
                    : observerDistance > 30f
                        ? Mathf.Min(36, state.BaseMaximumParticles)
                        : state.BaseMaximumParticles;
                main.simulationSpace =
                    ParticleSystemSimulationSpace.World;
            }
        }

        static void ConfigureLights(
            EffectSlot slot,
            ModuleDestructionEffectDefinition definition)
        {
            for (int index = 0; index < slot.Lights.Length; index++)
            {
                Light light = slot.Lights[index];
                if (light == null)
                    continue;
                bool useLight =
                    definition.Tier ==
                    ModuleDestructionEffectTier.Core &&
                    index == 0;
                light.enabled = useLight;
                if (!useLight)
                    continue;
                light.range =
                    Mathf.Min(7f, Mathf.Max(3f, light.range));
                light.intensity =
                    Mathf.Min(2.2f, Mathf.Max(1.2f, light.intensity));
            }
        }

        void Update()
        {
            float now = Time.time;
            for (int index = slots.Count - 1; index >= 0; index--)
            {
                EffectSlot slot = slots[index];
                if (slot.Root == null)
                {
                    slots.RemoveAt(index);
                    continue;
                }
                if (!slot.Active)
                    continue;
                if (slot.LightEndsAt > 0f &&
                    now >= slot.LightEndsAt)
                {
                    foreach (Light light in slot.Lights)
                        if (light != null)
                            light.enabled = false;
                    slot.LightEndsAt = 0f;
                }
                if (now >= slot.EndsAt)
                    StopSlot(slot);
            }
            for (int index = severFlashes.Count - 1;
                 index >= 0;
                 index--)
            {
                SeverFlashSlot flash = severFlashes[index];
                if (flash.Line == null)
                {
                    severFlashes.RemoveAt(index);
                    continue;
                }
                if (!flash.Active)
                    continue;
                if (now >= flash.EndsAt)
                {
                    flash.Line.enabled = false;
                    flash.Active = false;
                    continue;
                }
                UpdateSeverFlash(flash);
            }
        }

        void StopSlot(EffectSlot slot)
        {
            if (slot == null)
                return;
            if (slot.Particles != null)
            {
                foreach (ParticleState state in slot.Particles)
                {
                    if (state.Particle == null)
                        continue;
                    state.Particle.Stop(
                        true,
                        ParticleSystemStopBehavior.StopEmittingAndClear);
                    state.Particle.Clear(true);
                }
            }
            if (slot.Lights != null)
            {
                foreach (Light light in slot.Lights)
                    if (light != null)
                        light.enabled = false;
            }
            if (slot.Root != null)
                slot.Root.SetActive(false);
            slot.Active = false;
            slot.EndsAt = 0f;
            slot.LightEndsAt = 0f;
        }

        SeverFlashSlot CreateSeverFlash()
        {
            if (severMaterial == null)
            {
                Shader shader =
                    Shader.Find("Sprites/Default") ??
                    Shader.Find(
                        "Universal Render Pipeline/Unlit") ??
                    Shader.Find("Unlit/Color");
                if (shader == null ||
                    !shader.isSupported ||
                    Contains(shader.name, "InternalErrorShader"))
                {
                    if (!loggedSeverUnavailable)
                    {
                        loggedSeverUnavailable = true;
                        Debug.LogError(
                            "[Combat VFX] Sever flash requires a supported " +
                            "unlit line shader; the visual was rejected.",
                            this);
                    }
                    return null;
                }
                severMaterial = new Material(shader)
                {
                    name = "RuntimeSeverFlashMaterial"
                };
            }
            GameObject root = new GameObject("SeverFlash");
            root.transform.SetParent(transform, false);
            LineRenderer line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = severMaterial;
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = 24;
            line.numCapVertices = 2;
            line.enabled = false;
            var slot = new SeverFlashSlot { Line = line };
            severFlashes.Add(slot);
            return slot;
        }

        static void UpdateSeverFlash(SeverFlashSlot slot)
        {
            float duration =
                Mathf.Max(0.01f, slot.EndsAt - slot.StartedAt);
            float progress = Mathf.Clamp01(
                (Time.time - slot.StartedAt) / duration);
            Vector3 tangent =
                Vector3.Cross(slot.Normal, Vector3.up);
            if (tangent.sqrMagnitude < 0.001f)
                tangent = Vector3.Cross(slot.Normal, Vector3.right);
            tangent.Normalize();
            Vector3 bitangent =
                Vector3.Cross(slot.Normal, tangent).normalized;
            float radius = Mathf.Lerp(0.12f, 0.8f, progress);
            for (int index = 0;
                 index < slot.Line.positionCount;
                 index++)
            {
                float angle =
                    index /
                    (float)slot.Line.positionCount *
                    Mathf.PI * 2f;
                slot.Line.SetPosition(
                    index,
                    slot.Center +
                    (tangent * Mathf.Cos(angle) +
                     bitangent * Mathf.Sin(angle)) *
                    radius);
            }
            float alpha = 1f - progress;
            Color color =
                new Color(1f, 0.68f, 0.16f, alpha);
            slot.Line.startColor = color;
            slot.Line.endColor = color;
            slot.Line.startWidth =
                Mathf.Lerp(0.075f, 0.015f, progress);
            slot.Line.endWidth = slot.Line.startWidth;
        }

        static bool InvalidRenderer(Renderer renderer)
        {
            if (renderer == null)
                return true;

            if (renderer is ParticleSystemRenderer particleRenderer)
            {
                if (InvalidMaterial(particleRenderer.sharedMaterial))
                    return true;
                ParticleSystem particle =
                    particleRenderer.GetComponent<ParticleSystem>();
                return particle != null &&
                       particle.trails.enabled &&
                       InvalidMaterial(particleRenderer.trailMaterial);
            }

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
                return true;
            bool hasMaterial = false;
            foreach (Material material in materials)
            {
                if (material == null)
                    continue;
                hasMaterial = true;
                if (InvalidMaterial(material))
                    return true;
            }
            return !hasMaterial;
        }

        static bool InvalidMaterial(Material material)
        {
            if (material == null || material.shader == null)
                return true;
            string shaderName = material.shader.name ?? string.Empty;
            return !material.shader.isSupported ||
                   Contains(shaderName, "InternalErrorShader");
        }

        static bool IsMinor(string value)
        {
            return Contains(value, "spark") ||
                   Contains(value, "trail") ||
                   Contains(value, "debris") ||
                   Contains(value, "black");
        }

        static bool IsSmoke(string value)
        {
            return Contains(value, "smoke") ||
                   Contains(value, "black");
        }

        static bool Contains(string source, string fragment)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(
                       fragment,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void PruneDestroyedSlots()
        {
            for (int index = slots.Count - 1; index >= 0; index--)
                if (slots[index].Root == null)
                    slots.RemoveAt(index);
        }

        static float ResolveObserverDistance(Vector3 point)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                Camera[] cameras = Camera.allCameras;
                if (cameras.Length > 0)
                    camera = cameras[0];
            }
            return camera != null
                ? Vector3.Distance(camera.transform.position, point)
                : 0f;
        }

        void OnDestroy()
        {
            Clear();
            if (severMaterial == null)
                return;
            if (Application.isPlaying)
                Destroy(severMaterial);
            else
                DestroyImmediate(severMaterial);
            severMaterial = null;
        }
    }
}
