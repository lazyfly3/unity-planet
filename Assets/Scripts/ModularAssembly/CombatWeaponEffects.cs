using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    public enum CombatWeaponEffectKind
    {
        Kinetic,
        AntiAir,
        Sniper,
        Energy,
        Missile
    }

    public readonly struct CombatWeaponEffectProfile
    {
        public readonly CombatWeaponEffectKind Kind;
        public readonly float MuzzleFlashSize;
        public readonly float MuzzleLength;
        public readonly float ImpactCoreRadius;
        public readonly int MuzzleSparkCount;
        public readonly int ImpactSparkCount;
        public readonly bool UsesSmoke;
        public readonly bool UsesPressureRing;

        CombatWeaponEffectProfile(
            CombatWeaponEffectKind kind,
            float muzzleFlashSize,
            float muzzleLength,
            float impactCoreRadius,
            int muzzleSparkCount,
            int impactSparkCount,
            bool usesSmoke,
            bool usesPressureRing)
        {
            Kind = kind;
            MuzzleFlashSize = muzzleFlashSize;
            MuzzleLength = muzzleLength;
            ImpactCoreRadius = impactCoreRadius;
            MuzzleSparkCount = muzzleSparkCount;
            ImpactSparkCount = impactSparkCount;
            UsesSmoke = usesSmoke;
            UsesPressureRing = usesPressureRing;
        }

        public static CombatWeaponEffectProfile Resolve(
            string sourceEffect,
            float gameplayRadius = 0f)
        {
            string key = (sourceEffect ?? string.Empty).ToLowerInvariant();
            if (key.Contains("missile") ||
                key.Contains("rocket"))
            {
                return new CombatWeaponEffectProfile(
                    CombatWeaponEffectKind.Missile,
                    0.48f,
                    0.82f,
                    Mathf.Clamp(
                        Mathf.Max(0.1f, gameplayRadius) * 0.34f,
                        1.05f,
                        1.8f),
                    5,
                    26,
                    true,
                    true);
            }
            if (key.Contains("energy") ||
                key.Contains("plasma") ||
                key.Contains("psy") ||
                key.Contains("laser"))
            {
                return new CombatWeaponEffectProfile(
                    CombatWeaponEffectKind.Energy,
                    0.56f,
                    0.92f,
                    Mathf.Clamp(
                        Mathf.Max(0.1f, gameplayRadius) * 0.36f,
                        0.68f,
                        1.25f),
                    6,
                    18,
                    false,
                    true);
            }
            if (key.Contains("sniper"))
            {
                return new CombatWeaponEffectProfile(
                    CombatWeaponEffectKind.Sniper,
                    0.52f,
                    1.25f,
                    0.78f,
                    5,
                    20,
                    true,
                    true);
            }
            if (key.Contains("antiair"))
            {
                return new CombatWeaponEffectProfile(
                    CombatWeaponEffectKind.AntiAir,
                    0.46f,
                    1.05f,
                    Mathf.Clamp(
                        Mathf.Max(0.1f, gameplayRadius) * 0.34f,
                        0.52f,
                        0.9f),
                    6,
                    18,
                    true,
                    true);
            }
            return new CombatWeaponEffectProfile(
                CombatWeaponEffectKind.Kinetic,
                key.Contains("gatlin") ? 0.32f : 0.36f,
                key.Contains("gatlin") ? 0.72f : 0.82f,
                key.Contains("gatlin") ? 0.36f : 0.42f,
                4,
                key.Contains("gatlin") ? 12 : 14,
                true,
                false);
        }
    }

    // Purely visual, world-space feedback. It intentionally owns no collider,
    // Rigidbody, damage callback, or force application.
    [DisallowMultipleComponent]
    public sealed class CombatWeaponEffectPool : MonoBehaviour
    {
        public const int MaximumParticleSlots = 36;
        public const int MaximumLineSlots = 16;
        const int InitialParticleSlots = 12;
        const int InitialLineSlots = 8;

        const string MaterialRoot =
            "CombatFeedback/NeoX/Materials/";
        const string MetalFlash =
            MaterialRoot + "NeoX_metal_hit_01";
        const string MetalTrail =
            MaterialRoot + "NeoX_metal_hit_02";
        const string MetalSmoke =
            MaterialRoot + "NeoX_metal_hit_00";
        const string SniperFlash =
            MaterialRoot + "NeoX_sniper_hit_02";
        const string SniperTrail =
            MaterialRoot + "NeoX_sniper_hit_03";
        const string EnergyFlash =
            MaterialRoot + "NeoX_energy_impact_10";
        const string EnergyTrail =
            MaterialRoot + "NeoX_energy_impact_03";
        const string MissileFlash =
            MaterialRoot + "NeoX_missile_explosion_04";
        const string MissileTrail =
            MaterialRoot + "NeoX_missile_explosion_05";
        const string MissileSmoke =
            MaterialRoot + "NeoX_missile_explosion_02";

        enum LineEffectKind
        {
            Muzzle,
            Ring
        }

        sealed class ParticleSlot
        {
            public GameObject Root;
            public ParticleSystem System;
            public ParticleSystemRenderer Renderer;
            public float EndsAt;
        }

        sealed class LineSlot
        {
            public GameObject Root;
            public LineRenderer Line;
            public LineEffectKind Kind;
            public Vector3 Start;
            public Vector3 End;
            public Vector3 Center;
            public Vector3 Normal;
            public Color Color;
            public float StartRadius;
            public float EndRadius;
            public float StartWidth;
            public float EndWidth;
            public float StartedAt;
            public float EndsAt;
        }

        readonly Dictionary<string, Material> materials =
            new Dictionary<string, Material>(
                StringComparer.OrdinalIgnoreCase);
        readonly List<ParticleSlot> particleSlots =
            new List<ParticleSlot>();
        readonly List<LineSlot> lineSlots =
            new List<LineSlot>();

        Material lineMaterial;
        bool prewarmed;
        readonly HashSet<CombatWeaponEffectKind> loggedUnavailableFamilies =
            new HashSet<CombatWeaponEffectKind>();

        public bool IsReady { get; private set; }
        public int ParticleSlotCount => particleSlots.Count;
        public int LineSlotCount => lineSlots.Count;

        public void Prewarm()
        {
            if (prewarmed)
                return;
            prewarmed = true;

            string[] required =
            {
                MetalFlash,
                MetalTrail,
                MetalSmoke,
                SniperFlash,
                SniperTrail,
                EnergyFlash,
                EnergyTrail,
                MissileFlash,
                MissileTrail,
                MissileSmoke
            };
            foreach (string path in required)
            {
                Material material = Resources.Load<Material>(path);
                materials[path] = material;
            }

            Shader lineShader =
                Shader.Find("Sprites/Default") ??
                Shader.Find("Particles/Standard Unlit") ??
                Shader.Find("Unlit/Color");
            if (lineShader == null ||
                !lineShader.isSupported ||
                ContainsInternalError(lineShader.name))
            {
                IsReady = false;
                return;
            }
            lineMaterial = new Material(lineShader)
            {
                name = "RuntimeCombatVfxLine"
            };

            while (particleSlots.Count < InitialParticleSlots)
                CreateParticleSlot();
            while (lineSlots.Count < InitialLineSlots)
                CreateLineSlot();

            IsReady =
                FamilyIsReady(CombatWeaponEffectKind.Kinetic) &&
                FamilyIsReady(CombatWeaponEffectKind.Sniper) &&
                FamilyIsReady(CombatWeaponEffectKind.Energy) &&
                FamilyIsReady(CombatWeaponEffectKind.Missile);
        }

        public bool SpawnMuzzle(
            Vector3 position,
            Vector3 direction,
            Color requestedColor,
            string sourceEffect,
            Transform weaponRoot)
        {
            Prewarm();
            CombatWeaponEffectProfile profile =
                CombatWeaponEffectProfile.Resolve(sourceEffect);
            if (!EnsureFamilyReady(profile.Kind))
                return false;
            Vector3 forward = SafeDirection(direction, Vector3.forward);
            Color coreColor =
                ResolveCoreColor(profile.Kind, requestedColor);

            bool visible = EmitFlash(
                position,
                coreColor,
                profile.MuzzleFlashSize,
                0.085f,
                FlashMaterial(profile.Kind),
                "MuzzleFlash");
            visible |= EmitSparks(
                position,
                forward,
                coreColor,
                profile.MuzzleSparkCount,
                profile.Kind == CombatWeaponEffectKind.Missile
                    ? 1.5f
                    : 3.5f,
                0.1f,
                0.2f,
                0.02f,
                0.045f,
                TrailMaterial(profile.Kind),
                "MuzzleSparks",
                0.22f);
            visible |= SpawnMuzzleLine(
                position,
                position + forward * profile.MuzzleLength,
                coreColor,
                Mathf.Clamp(
                    profile.MuzzleFlashSize * 0.42f,
                    0.11f,
                    0.28f),
                0.08f);

            if (profile.Kind == CombatWeaponEffectKind.Missile)
            {
                visible |= EmitSmoke(
                    position - forward * 0.06f,
                    -forward,
                    2,
                    0.18f,
                    0.32f,
                    0.35f,
                    MissileSmoke,
                    "MuzzleSmoke");
            }
            if (visible)
            {
                Forge3DWeaponAnimator.Trigger(
                    weaponRoot,
                    forward,
                    sourceEffect);
            }
            return visible;
        }

        public bool SpawnImpact(
            Vector3 position,
            Vector3 normal,
            Color requestedColor,
            string sourceEffect,
            float gameplayRadius)
        {
            Prewarm();
            CombatWeaponEffectProfile profile =
                CombatWeaponEffectProfile.Resolve(
                    sourceEffect,
                    gameplayRadius);
            if (!EnsureFamilyReady(profile.Kind))
                return false;
            Vector3 surfaceNormal = SafeDirection(normal, Vector3.up);
            Color coreColor =
                ResolveCoreColor(profile.Kind, requestedColor);

            float flashLifetime =
                profile.Kind == CombatWeaponEffectKind.Missile
                    ? 0.12f
                    : 0.085f;
            bool visible = EmitFlash(
                position + surfaceNormal * 0.015f,
                coreColor,
                profile.ImpactCoreRadius * 2f,
                flashLifetime,
                FlashMaterial(profile.Kind),
                "ImpactCore");

            float sparkSpeed =
                profile.Kind == CombatWeaponEffectKind.Missile
                    ? 8f
                    : profile.Kind == CombatWeaponEffectKind.Sniper
                        ? 10f
                        : 6.4f;
            visible |= EmitSparks(
                position + surfaceNormal * 0.02f,
                surfaceNormal,
                coreColor,
                profile.ImpactSparkCount,
                sparkSpeed,
                profile.Kind == CombatWeaponEffectKind.Missile
                    ? 0.24f
                    : 0.18f,
                profile.Kind == CombatWeaponEffectKind.Missile
                    ? 0.48f
                    : 0.42f,
                profile.Kind == CombatWeaponEffectKind.Missile
                    ? 0.035f
                    : 0.035f,
                profile.Kind == CombatWeaponEffectKind.Missile
                    ? 0.085f
                    : 0.095f,
                TrailMaterial(profile.Kind),
                "ImpactSparks",
                profile.Kind == CombatWeaponEffectKind.Sniper
                    ? 0.38f
                    : 0.62f);

            if (profile.UsesSmoke)
            {
                int smokeCount =
                    profile.Kind == CombatWeaponEffectKind.Missile
                        ? 7
                        : profile.Kind == CombatWeaponEffectKind.Kinetic
                            ? 2
                            : 3;
                visible |= EmitSmoke(
                    position + surfaceNormal * 0.03f,
                    surfaceNormal,
                    smokeCount,
                    profile.ImpactCoreRadius * 0.6f,
                    profile.ImpactCoreRadius * 1.15f,
                    profile.Kind == CombatWeaponEffectKind.Missile
                        ? 0.72f
                        : 0.46f,
                    SmokeMaterial(profile.Kind),
                    "ImpactSmoke");
            }

            if (profile.UsesPressureRing)
            {
                float ringRadius = ResolveRingRadius(
                    profile,
                    gameplayRadius);
                visible |= SpawnRing(
                    position + surfaceNormal * 0.025f,
                    surfaceNormal,
                    RingColor(profile.Kind, requestedColor),
                    Mathf.Max(0.07f, ringRadius * 0.06f),
                    ringRadius,
                    profile.Kind == CombatWeaponEffectKind.Missile
                         ? 0.36f
                         : 0.24f);
            }
            return visible;
        }

        bool EmitFlash(
            Vector3 position,
            Color color,
            float size,
            float lifetime,
            string materialPath,
            string layerName)
        {
            ParticleSlot slot = PrepareParticleSlot(
                materialPath,
                ParticleSystemRenderMode.Billboard,
                layerName,
                lifetime);
            if (slot == null)
                return false;

            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = Vector3.zero,
                startColor = color,
                startLifetime = lifetime,
                startSize = Mathf.Max(0.02f, size),
                randomSeed = Seed(position, 1)
            };
            slot.System.Emit(emit, 1);
            return true;
        }

        bool EmitSparks(
            Vector3 position,
            Vector3 direction,
            Color color,
            int count,
            float maximumSpeed,
            float minimumLifetime,
            float maximumLifetime,
            float minimumSize,
            float maximumSize,
            string materialPath,
            string layerName,
            float radialWeight)
        {
            ParticleSlot slot = PrepareParticleSlot(
                materialPath,
                ParticleSystemRenderMode.Stretch,
                layerName,
                maximumLifetime);
            if (slot == null)
                return false;
            slot.Renderer.alignment =
                ParticleSystemRenderSpace.Velocity;
            slot.Renderer.velocityScale = 0.1f;
            slot.Renderer.lengthScale = 2.2f;

            Vector3 forward = SafeDirection(direction, Vector3.up);
            BuildBasis(forward, out Vector3 tangent, out Vector3 bitangent);
            int safeCount = Mathf.Clamp(count, 1, 32);
            for (int index = 0; index < safeCount; index++)
            {
                float fraction = (index + 0.5f) / safeCount;
                float angle = index * 2.39996323f;
                Vector3 radial =
                    tangent * Mathf.Cos(angle) +
                    bitangent * Mathf.Sin(angle);
                Vector3 velocityDirection =
                    (forward +
                     radial * radialWeight *
                     Mathf.Lerp(0.35f, 1f, fraction)).normalized;
                float speed = maximumSpeed *
                              Mathf.Lerp(0.55f, 1f, fraction);
                ParticleSystem.EmitParams emit =
                    new ParticleSystem.EmitParams
                    {
                        position = position,
                        velocity = velocityDirection * speed,
                        startColor = color,
                        startLifetime = Mathf.Lerp(
                            minimumLifetime,
                            maximumLifetime,
                            fraction),
                        startSize = Mathf.Lerp(
                            minimumSize,
                            maximumSize,
                            1f - fraction),
                        randomSeed = Seed(position, index + 17)
                };
                slot.System.Emit(emit, 1);
            }
            return true;
        }

        bool EmitSmoke(
            Vector3 position,
            Vector3 direction,
            int count,
            float minimumSize,
            float maximumSize,
            float lifetime,
            string materialPath,
            string layerName)
        {
            ParticleSlot slot = PrepareParticleSlot(
                materialPath,
                ParticleSystemRenderMode.Billboard,
                layerName,
                lifetime);
            if (slot == null)
                return false;

            Vector3 forward = SafeDirection(direction, Vector3.up);
            BuildBasis(forward, out Vector3 tangent, out Vector3 bitangent);
            int safeCount = Mathf.Clamp(count, 1, 12);
            Color smoke = new Color(0.48f, 0.5f, 0.53f, 0.52f);
            for (int index = 0; index < safeCount; index++)
            {
                float fraction = (index + 0.5f) / safeCount;
                float angle = index * 2.39996323f;
                Vector3 drift =
                    tangent * Mathf.Cos(angle) +
                    bitangent * Mathf.Sin(angle);
                ParticleSystem.EmitParams emit =
                    new ParticleSystem.EmitParams
                    {
                        position = position,
                        velocity =
                            forward * Mathf.Lerp(0.35f, 0.9f, fraction) +
                            drift * 0.22f,
                        startColor = smoke,
                        startLifetime = lifetime *
                                        Mathf.Lerp(0.8f, 1f, fraction),
                        startSize = Mathf.Lerp(
                            minimumSize,
                            maximumSize,
                            fraction),
                        randomSeed = Seed(position, index + 73)
                };
                slot.System.Emit(emit, 1);
            }
            return true;
        }

        ParticleSlot PrepareParticleSlot(
            string materialPath,
            ParticleSystemRenderMode renderMode,
            string layerName,
            float lifetime)
        {
            if (!materials.TryGetValue(
                    materialPath,
                    out Material material) ||
                !IsUsable(material))
                return null;

            ParticleSlot slot = AcquireParticleSlot();
            if (slot == null)
                return null;

            slot.Root.name = "CombatVfx_" + layerName;
            slot.Root.SetActive(true);
            slot.System.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = slot.System.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace =
                ParticleSystemSimulationSpace.World;
            main.scalingMode =
                ParticleSystemScalingMode.Local;
            main.maxParticles = 64;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission =
                slot.System.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = slot.System.shape;
            shape.enabled = false;
            ParticleSystem.CollisionModule collision =
                slot.System.collision;
            collision.enabled = false;
            ParticleSystem.TrailModule trails =
                slot.System.trails;
            trails.enabled = false;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime =
                slot.System.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color =
                new ParticleSystem.MinMaxGradient(FadeGradient());

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime =
                slot.System.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size =
                new ParticleSystem.MinMaxCurve(
                    1f,
                    SizeCurve(renderMode));

            slot.Renderer.enabled = true;
            slot.Renderer.sharedMaterial = material;
            slot.Renderer.renderMode = renderMode;
            slot.Renderer.alignment =
                ParticleSystemRenderSpace.View;
            slot.System.Play(false);
            slot.EndsAt =
                Time.time + Mathf.Max(0.08f, lifetime) + 0.08f;
            return slot;
        }

        ParticleSlot AcquireParticleSlot()
        {
            particleSlots.RemoveAll(
                item => item == null || item.Root == null);
            ParticleSlot oldest = null;
            foreach (ParticleSlot candidate in particleSlots)
            {
                if (!candidate.Root.activeSelf)
                    return candidate;
                if (oldest == null ||
                    candidate.EndsAt < oldest.EndsAt)
                    oldest = candidate;
            }
            if (particleSlots.Count < MaximumParticleSlots)
                return CreateParticleSlot();
            Deactivate(oldest);
            return oldest;
        }

        ParticleSlot CreateParticleSlot()
        {
            GameObject root = new GameObject("CombatVfx_Particle");
            root.transform.SetParent(transform, false);
            ParticleSystem system = root.AddComponent<ParticleSystem>();
            system.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystemRenderer renderer =
                root.GetComponent<ParticleSystemRenderer>();
            var slot = new ParticleSlot
            {
                Root = root,
                System = system,
                Renderer = renderer
            };
            root.SetActive(false);
            particleSlots.Add(slot);
            return slot;
        }

        bool SpawnMuzzleLine(
            Vector3 start,
            Vector3 end,
            Color color,
            float width,
            float lifetime)
        {
            LineSlot slot = PrepareLineSlot(
                LineEffectKind.Muzzle,
                lifetime);
            if (slot == null)
                return false;
            slot.Start = start;
            slot.End = end;
            slot.Color = color;
            slot.StartWidth = width;
            slot.EndWidth = 0.01f;
            slot.Line.loop = false;
            slot.Line.positionCount = 2;
            slot.Line.SetPosition(0, start);
            slot.Line.SetPosition(1, end);
            UpdateLine(slot);
            return true;
        }

        bool SpawnRing(
            Vector3 center,
            Vector3 normal,
            Color color,
            float startRadius,
            float endRadius,
            float lifetime)
        {
            LineSlot slot = PrepareLineSlot(
                LineEffectKind.Ring,
                lifetime);
            if (slot == null)
                return false;
            slot.Center = center;
            slot.Normal = SafeDirection(normal, Vector3.up);
            slot.Color = color;
            slot.StartRadius = startRadius;
            slot.EndRadius = Mathf.Max(startRadius, endRadius);
            slot.StartWidth = Mathf.Clamp(
                endRadius * 0.025f,
                0.035f,
                0.12f);
            slot.EndWidth = 0.008f;
            slot.Line.loop = true;
            slot.Line.positionCount = 32;
            UpdateLine(slot);
            return true;
        }

        LineSlot PrepareLineSlot(
            LineEffectKind kind,
            float lifetime)
        {
            if (!IsUsable(lineMaterial))
                return null;
            LineSlot slot = AcquireLineSlot();
            if (slot == null)
                return null;

            slot.Kind = kind;
            slot.StartedAt = Time.time;
            slot.EndsAt =
                Time.time + Mathf.Max(0.05f, lifetime);
            slot.Root.name = kind == LineEffectKind.Ring
                ? "CombatVfx_PressureRing"
                : "CombatVfx_MuzzleStreak";
            slot.Root.SetActive(true);
            slot.Line.enabled = true;
            return slot;
        }

        LineSlot AcquireLineSlot()
        {
            lineSlots.RemoveAll(
                item => item == null || item.Root == null);
            LineSlot oldest = null;
            foreach (LineSlot candidate in lineSlots)
            {
                if (!candidate.Root.activeSelf)
                    return candidate;
                if (oldest == null ||
                    candidate.EndsAt < oldest.EndsAt)
                    oldest = candidate;
            }
            if (lineSlots.Count < MaximumLineSlots)
                return CreateLineSlot();
            Deactivate(oldest);
            return oldest;
        }

        LineSlot CreateLineSlot()
        {
            GameObject root = new GameObject("CombatVfx_Line");
            root.transform.SetParent(transform, false);
            LineRenderer line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = lineMaterial;
            line.useWorldSpace = true;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.enabled = false;
            var slot = new LineSlot { Root = root, Line = line };
            root.SetActive(false);
            lineSlots.Add(slot);
            return slot;
        }

        void Update()
        {
            float now = Time.time;
            foreach (ParticleSlot slot in particleSlots)
            {
                if (slot.Root == null ||
                    !slot.Root.activeSelf ||
                    now < slot.EndsAt)
                    continue;
                slot.System.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
                slot.Root.SetActive(false);
            }

            foreach (LineSlot slot in lineSlots)
            {
                if (slot.Root == null ||
                    !slot.Root.activeSelf)
                    continue;
                if (now >= slot.EndsAt)
                {
                    slot.Line.enabled = false;
                    slot.Root.SetActive(false);
                    continue;
                }
                UpdateLine(slot);
            }
        }

        public void Clear()
        {
            foreach (ParticleSlot slot in particleSlots)
                Deactivate(slot);
            foreach (LineSlot slot in lineSlots)
                Deactivate(slot);
        }

        public void ShiftWorld(Vector3 delta)
        {
            if (delta.sqrMagnitude < 0.000001f)
                return;
            foreach (LineSlot slot in lineSlots)
            {
                if (slot == null)
                    continue;
                slot.Start += delta;
                slot.End += delta;
                slot.Center += delta;
            }
        }

        static void Deactivate(ParticleSlot slot)
        {
            if (slot?.Root == null)
                return;
            if (slot.System != null)
            {
                slot.System.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
                slot.System.Clear(true);
            }
            slot.EndsAt = 0f;
            slot.Root.SetActive(false);
        }

        static void Deactivate(LineSlot slot)
        {
            if (slot?.Root == null)
                return;
            if (slot.Line != null)
            {
                slot.Line.enabled = false;
                slot.Line.positionCount = 0;
            }
            slot.StartedAt = 0f;
            slot.EndsAt = 0f;
            slot.Root.SetActive(false);
        }

        static void UpdateLine(LineSlot slot)
        {
            float duration =
                Mathf.Max(0.01f, slot.EndsAt - slot.StartedAt);
            float progress = Mathf.Clamp01(
                (Time.time - slot.StartedAt) / duration);
            float eased = 1f - (1f - progress) * (1f - progress);
            float alpha = 1f - eased;
            Color color = slot.Color;
            color.a *= alpha;
            slot.Line.startColor = color;
            slot.Line.endColor = new Color(
                color.r,
                color.g,
                color.b,
                color.a * 0.35f);
            float width = Mathf.Lerp(
                slot.StartWidth,
                slot.EndWidth,
                eased);
            slot.Line.startWidth = width;
            slot.Line.endWidth =
                slot.Kind == LineEffectKind.Muzzle
                    ? Mathf.Max(0.004f, width * 0.18f)
                    : width;

            if (slot.Kind == LineEffectKind.Muzzle)
            {
                slot.Line.SetPosition(
                    0,
                    Vector3.Lerp(slot.Start, slot.End, progress * 0.18f));
                slot.Line.SetPosition(1, slot.End);
                return;
            }

            BuildBasis(
                slot.Normal,
                out Vector3 tangent,
                out Vector3 bitangent);
            float radius = Mathf.Lerp(
                slot.StartRadius,
                slot.EndRadius,
                eased);
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
        }

        static Gradient FadeGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.9f, 0.2f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        static AnimationCurve SizeCurve(
            ParticleSystemRenderMode renderMode)
        {
            return renderMode == ParticleSystemRenderMode.Billboard
                ? new AnimationCurve(
                    new Keyframe(0f, 0.72f),
                    new Keyframe(0.12f, 1f),
                    new Keyframe(1f, 0f))
                : new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(0.72f, 0.65f),
                    new Keyframe(1f, 0f));
        }

        string FlashMaterial(CombatWeaponEffectKind kind)
        {
            switch (kind)
            {
                case CombatWeaponEffectKind.Sniper:
                    return SniperFlash;
                case CombatWeaponEffectKind.Energy:
                    return EnergyFlash;
                case CombatWeaponEffectKind.Missile:
                    return MissileFlash;
                default:
                    return MetalFlash;
            }
        }

        string TrailMaterial(CombatWeaponEffectKind kind)
        {
            switch (kind)
            {
                case CombatWeaponEffectKind.Sniper:
                    return SniperTrail;
                case CombatWeaponEffectKind.Energy:
                    return EnergyTrail;
                case CombatWeaponEffectKind.Missile:
                    return MissileTrail;
                default:
                    return MetalTrail;
            }
        }

        static string SmokeMaterial(CombatWeaponEffectKind kind)
        {
            return kind == CombatWeaponEffectKind.Missile
                ? MissileSmoke
                : MetalSmoke;
        }

        bool EnsureFamilyReady(CombatWeaponEffectKind kind)
        {
            if (FamilyIsReady(kind))
                return true;
            if (!loggedUnavailableFamilies.Add(kind))
                return false;

            var missing = new List<string>();
            AddIfUnavailable(missing, FlashMaterial(kind));
            AddIfUnavailable(missing, TrailMaterial(kind));
            if (kind != CombatWeaponEffectKind.Energy)
                AddIfUnavailable(missing, SmokeMaterial(kind));
            if (!IsUsable(lineMaterial))
                missing.Add("runtime line shader");
            Debug.LogError(
                "[CombatVFX] " + kind +
                " feedback is unavailable because required visual resources " +
                "are invalid: " + string.Join(", ", missing) +
                ". The normal fallback path will be used.",
                this);
            return false;
        }

        bool FamilyIsReady(CombatWeaponEffectKind kind)
        {
            return IsUsable(lineMaterial) &&
                   MaterialPathIsUsable(FlashMaterial(kind)) &&
                   MaterialPathIsUsable(TrailMaterial(kind)) &&
                   (kind == CombatWeaponEffectKind.Energy ||
                    MaterialPathIsUsable(SmokeMaterial(kind)));
        }

        bool MaterialPathIsUsable(string path)
        {
            return materials.TryGetValue(path, out Material material) &&
                   IsUsable(material);
        }

        void AddIfUnavailable(List<string> missing, string path)
        {
            if (!MaterialPathIsUsable(path))
                missing.Add(path);
        }

        static float ResolveRingRadius(
            CombatWeaponEffectProfile profile,
            float gameplayRadius)
        {
            if (gameplayRadius > 0.05f)
                return gameplayRadius;
            switch (profile.Kind)
            {
                case CombatWeaponEffectKind.Sniper:
                    return 0.68f;
                case CombatWeaponEffectKind.Energy:
                    return 0.9f;
                case CombatWeaponEffectKind.AntiAir:
                    return 0.75f;
                default:
                    return profile.ImpactCoreRadius * 1.4f;
            }
        }

        static Color ResolveCoreColor(
            CombatWeaponEffectKind kind,
            Color requested)
        {
            Color identity;
            switch (kind)
            {
                case CombatWeaponEffectKind.Energy:
                    identity = new Color(0.3f, 0.92f, 1f, 1f);
                    break;
                case CombatWeaponEffectKind.Sniper:
                    identity = new Color(0.72f, 0.95f, 1f, 1f);
                    break;
                case CombatWeaponEffectKind.Missile:
                    identity = new Color(1f, 0.48f, 0.12f, 1f);
                    break;
                case CombatWeaponEffectKind.AntiAir:
                    identity = new Color(1f, 0.6f, 0.16f, 1f);
                    break;
                default:
                    identity = new Color(1f, 0.78f, 0.24f, 1f);
                    break;
            }
            requested.a = 1f;
            Color result = Color.Lerp(identity, requested, 0.28f);
            result.a = 1f;
            return result;
        }

        static Color RingColor(
            CombatWeaponEffectKind kind,
            Color requested)
        {
            Color color = ResolveCoreColor(kind, requested);
            color = Color.Lerp(color, Color.white, 0.2f);
            color.a =
                kind == CombatWeaponEffectKind.Missile
                    ? 0.62f
                    : 0.82f;
            return color;
        }

        static bool IsUsable(Material material)
        {
            return material != null &&
                   material.shader != null &&
                   material.shader.isSupported &&
                   !ContainsInternalError(material.shader.name);
        }

        static bool ContainsInternalError(string shaderName)
        {
            return !string.IsNullOrEmpty(shaderName) &&
                   shaderName.IndexOf(
                       "InternalErrorShader",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static Vector3 SafeDirection(
            Vector3 direction,
            Vector3 fallback)
        {
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : fallback.normalized;
        }

        static void BuildBasis(
            Vector3 normal,
            out Vector3 tangent,
            out Vector3 bitangent)
        {
            Vector3 safeNormal = SafeDirection(normal, Vector3.up);
            tangent = Vector3.Cross(safeNormal, Vector3.up);
            if (tangent.sqrMagnitude < 0.001f)
                tangent = Vector3.Cross(safeNormal, Vector3.right);
            tangent.Normalize();
            bitangent = Vector3.Cross(safeNormal, tangent).normalized;
        }

        static uint Seed(Vector3 position, int salt)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Mathf.RoundToInt(position.x * 100f);
                hash = hash * 31 + Mathf.RoundToInt(position.y * 100f);
                hash = hash * 31 + Mathf.RoundToInt(position.z * 100f);
                hash = hash * 31 + salt;
                return (uint)Mathf.Max(1, hash);
            }
        }

        void OnDestroy()
        {
            Clear();
            if (lineMaterial == null)
                return;
            if (Application.isPlaying)
                Destroy(lineMaterial);
            else
                DestroyImmediate(lineMaterial);
            lineMaterial = null;
        }
    }
}
