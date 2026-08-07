using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.CityPcg
{
    [DisallowMultipleComponent]
    public sealed class UrbanDestructionFamily : MonoBehaviour
    {
        [SerializeField] int familyId;

        public int FamilyId => familyId;

        public void Configure(int value)
        {
            familyId = value;
        }
    }

    /// <summary>
    /// Per-projectile guard: one crescent may cross several buildings, but it
    /// may advance each building's recursive slice tree by only one level.
    /// </summary>
    public sealed class UrbanEnergyBladePass
    {
        readonly HashSet<int> claimedFamilies = new HashSet<int>();

        public bool TryClaim(Collider collider)
        {
            int familyId =
                UrbanDestructionWorld.ResolveEnergyBladeFamilyId(collider);
            return familyId == 0 || claimedFamilies.Add(familyId);
        }
    }

    public enum UrbanDamageKind
    {
        StandardProjectile,
        Explosion,
        DemolitionProjectile,
        HighSpeedImpact,
        EnergyBlade
    }

    public readonly struct UrbanDamageRequest
    {
        public readonly Vector3 point;
        public readonly Vector3 normal;
        public readonly Vector3 direction;
        public readonly float damage;
        public readonly float radius;
        public readonly float impulse;
        public readonly UrbanDamageKind kind;
        public readonly GameObject source;

        public UrbanDamageRequest(
            Vector3 point,
            Vector3 normal,
            Vector3 direction,
            float damage,
            float radius,
            float impulse,
            UrbanDamageKind kind,
            GameObject source)
        {
            this.point = point;
            this.normal = normal.sqrMagnitude > 0.0001f
                ? normal.normalized
                : Vector3.up;
            this.direction = direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : -this.normal;
            this.damage = Mathf.Max(0f, damage);
            this.radius = Mathf.Max(0f, radius);
            this.impulse = Mathf.Max(0f, impulse);
            this.kind = kind;
            this.source = source;
        }
    }

    public interface IUrbanDestructible
    {
        Bounds DestructionBounds { get; }
        bool IsUrbanDestroyed { get; }
        bool ApplyUrbanDamage(in UrbanDamageRequest request);
    }

    [Serializable]
    public sealed class UrbanDestructionSettings
    {
        [Header("总预算")]
        [Range(8, 96)] public int maximumPhysicalDebris = 48;
        [Range(16, 192)] public int maximumVisualDebris = 96;
        [Range(2f, 12f)] public float debrisLifetime = 7.5f;
        [Range(0.25f, 3f)] public float physicalCollisionSeconds = 1.35f;

        [Header("撞击约束")]
        [Range(15f, 80f)] public float highSpeedImpactThreshold = 34f;
        [Range(0.1f, 3f)] public float impactDamageScale = 1f;

        [Header("结构约束")]
        [Range(0.1f, 1f)] public float demolitionIntegrityFraction = 0.48f;
        [Range(0.01f, 0.3f)] public float normalProjectileDamageScale = 0.08f;
        [Range(0.05f, 1f)] public float explosionDamageScale = 0.38f;

        [Header("Localized structural graph")]
        [Range(4, 6)] public int structuralColumns = 5;
        [Range(7f, 18f)] public float targetStructuralFloorHeight = 9f;
        [Range(0.1f, 0.45f)] public float neighbourSupportTransfer = 0.22f;
        [Range(4, 12)] public int maximumVisibleStructuralHoles = 8;

        public UrbanDestructionSettings ValidatedCopy()
        {
            return new UrbanDestructionSettings
            {
                maximumPhysicalDebris = Mathf.Clamp(
                    maximumPhysicalDebris, 8, 96),
                maximumVisualDebris = Mathf.Clamp(
                    maximumVisualDebris, 16, 192),
                debrisLifetime = Mathf.Clamp(debrisLifetime, 2f, 12f),
                physicalCollisionSeconds = Mathf.Clamp(
                    physicalCollisionSeconds, 0.25f, 3f),
                highSpeedImpactThreshold = Mathf.Clamp(
                    highSpeedImpactThreshold, 15f, 80f),
                impactDamageScale = Mathf.Clamp(impactDamageScale, 0.1f, 3f),
                demolitionIntegrityFraction = Mathf.Clamp(
                    demolitionIntegrityFraction, 0.1f, 1f),
                normalProjectileDamageScale = Mathf.Clamp(
                    normalProjectileDamageScale, 0.01f, 0.3f),
                explosionDamageScale = Mathf.Clamp(
                    explosionDamageScale, 0.05f, 1f),
                structuralColumns = Mathf.Clamp(structuralColumns, 4, 6),
                targetStructuralFloorHeight = Mathf.Clamp(
                    targetStructuralFloorHeight, 7f, 18f),
                neighbourSupportTransfer = Mathf.Clamp(
                    neighbourSupportTransfer, 0.1f, 0.45f),
                maximumVisibleStructuralHoles = Mathf.Clamp(
                    maximumVisibleStructuralHoles, 4, 12)
            };
        }
    }

    /// <summary>
    /// 城市破坏的唯一入口。它只登记城市建筑与城市装饰，不扫描或改写地面网格。
    /// </summary>
    public static class UrbanDestructionWorld
    {
        const int MaximumPersistentRuinSections = 384;

        static readonly List<UrbanDestructibleBuilding> Buildings =
            new List<UrbanDestructibleBuilding>(256);
        static readonly List<UrbanDestructibleDecoration> Decorations =
            new List<UrbanDestructibleDecoration>(256);
        static readonly List<UrbanDestructibleBridge> Bridges =
            new List<UrbanDestructibleBridge>(64);
        static readonly List<UrbanDestructibleRuinSection> RuinSections =
            new List<UrbanDestructibleRuinSection>(128);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Buildings.Clear();
            Decorations.Clear();
            Bridges.Clear();
            RuinSections.Clear();
        }

        internal static void Register(UrbanDestructibleBuilding building)
        {
            if (building != null && !Buildings.Contains(building))
                Buildings.Add(building);
        }

        internal static void Unregister(UrbanDestructibleBuilding building)
        {
            Buildings.Remove(building);
        }

        internal static void Register(UrbanDestructibleDecoration decoration)
        {
            if (decoration != null && !Decorations.Contains(decoration))
                Decorations.Add(decoration);
        }

        internal static void Unregister(UrbanDestructibleDecoration decoration)
        {
            Decorations.Remove(decoration);
        }

        internal static void Register(UrbanDestructibleBridge bridge)
        {
            if (bridge != null && !Bridges.Contains(bridge))
                Bridges.Add(bridge);
        }

        internal static void Unregister(UrbanDestructibleBridge bridge)
        {
            Bridges.Remove(bridge);
        }

        internal static void Register(UrbanDestructibleRuinSection section)
        {
            if (section != null && !RuinSections.Contains(section))
                RuinSections.Add(section);
        }

        internal static void Unregister(UrbanDestructibleRuinSection section)
        {
            RuinSections.Remove(section);
        }

        internal static int AvailableRuinSectionSlots(int requested)
        {
            RuinSections.RemoveAll(section => section == null);
            return Mathf.Clamp(
                MaximumPersistentRuinSections - RuinSections.Count,
                0,
                Mathf.Max(0, requested));
        }

        /// <summary>
        /// Returns a stable id shared by the authored building and every ruin
        /// section generated from it. A single crescent projectile uses this
        /// to cut one generation of a building only once; otherwise the same
        /// projectile immediately re-hits its freshly created child colliders
        /// and consumes several recursive generations in one flight.
        /// </summary>
        public static int ResolveEnergyBladeFamilyId(Collider collider)
        {
            if (collider == null)
                return 0;
            UrbanDestructionFamily family =
                collider.GetComponentInParent<UrbanDestructionFamily>();
            if (family != null && family.FamilyId != 0)
                return family.FamilyId;
            UrbanDestructibleBuilding building =
                collider.GetComponentInParent<UrbanDestructibleBuilding>();
            if (building != null)
                return building.GetInstanceID();
            UrbanDestructibleBridge bridge =
                collider.GetComponentInParent<UrbanDestructibleBridge>();
            if (bridge != null)
                return bridge.GetInstanceID();
            UrbanDestructibleRuinSection ruin =
                collider.GetComponentInParent<UrbanDestructibleRuinSection>();
            if (ruin == null)
                return 0;
            Transform root = ruin.transform;
            while (root.parent != null &&
                   root.parent.GetComponent<UrbanDestructionCoordinator>() == null)
            {
                root = root.parent;
            }
            return root.gameObject.GetInstanceID();
        }

        /// <summary>
        /// 普通武器仍会被建筑碰撞体阻挡并播放命中特效，但不会进入城市结构
        /// 破坏系统。只有明确标记为破坏弹、月牙光刃、高速撞击或合格爆炸的
        /// 事件可以改变建筑。
        /// </summary>
        public static bool TryApplyDirect(
            Collider collider,
            Vector3 point,
            Vector3 direction,
            float damage,
            GameObject source)
        {
            return false;
        }
        public static bool TryApplyDemolition(
            Collider collider,
            Vector3 point,
            Vector3 normal,
            Vector3 direction,
            float damage,
            float radius,
            GameObject source)
        {
            if (collider == null)
                return false;
            UrbanDestructibleRuinSection ruinSection =
                collider.GetComponentInParent<UrbanDestructibleRuinSection>();
            if (ruinSection != null)
            {
                return ruinSection.ApplyUrbanDamage(new UrbanDamageRequest(
                    point,
                    normal,
                    direction,
                    damage,
                    radius,
                    damage,
                    UrbanDamageKind.DemolitionProjectile,
                    source));
            }
            UrbanDestructibleBridge bridge =
                collider.GetComponentInParent<UrbanDestructibleBridge>();
            if (bridge != null)
            {
                return bridge.ApplyUrbanDamage(new UrbanDamageRequest(
                    point,
                    normal,
                    direction,
                    damage,
                    radius,
                    damage,
                    UrbanDamageKind.DemolitionProjectile,
                    source));
            }
            UrbanDestructibleBuilding building = ResolveBuilding(collider);
            if (building == null)
                return false;
            return building.ApplyUrbanDamage(new UrbanDamageRequest(
                point,
                normal,
                direction,
                damage,
                radius,
                damage,
                UrbanDamageKind.DemolitionProjectile,
                source));
        }

        public static bool TryApplyEnergyBlade(
            Collider collider,
            Vector3 point,
            Vector3 normal,
            Vector3 direction,
            float damage,
            float bladeWidth,
            GameObject source)
        {
            if (collider == null)
                return false;
            UrbanDestructibleRuinSection ruinSection =
                collider.GetComponentInParent<UrbanDestructibleRuinSection>();
            if (ruinSection != null)
            {
                return ruinSection.ApplyUrbanDamage(new UrbanDamageRequest(
                    point,
                    normal,
                    direction,
                    damage,
                    bladeWidth,
                    damage,
                    UrbanDamageKind.EnergyBlade,
                    source));
            }
            UrbanDestructibleBridge bridge =
                collider.GetComponentInParent<UrbanDestructibleBridge>();
            if (bridge != null)
            {
                return bridge.ApplyUrbanDamage(new UrbanDamageRequest(
                    point,
                    normal,
                    direction,
                    damage,
                    bladeWidth,
                    damage,
                    UrbanDamageKind.EnergyBlade,
                    source));
            }
            UrbanDestructibleBuilding building = ResolveBuilding(collider);
            if (building == null)
                return false;
            return building.ApplyUrbanDamage(new UrbanDamageRequest(
                point,
                normal,
                direction,
                damage,
                bladeWidth,
                damage,
                UrbanDamageKind.EnergyBlade,
                source));
        }

        public static int ApplyExplosion(
            Vector3 center,
            float radius,
            float damage,
            GameObject source)
        {
            if (radius <= 0.01f || damage <= 0f)
                return 0;
            int affected = 0;
            int existingRuinSectionCount = RuinSections.Count;
            for (int index = Buildings.Count - 1; index >= 0; index--)
            {
                UrbanDestructibleBuilding building = Buildings[index];
                if (building == null)
                {
                    Buildings.RemoveAt(index);
                    continue;
                }
                if (building.IsUrbanDestroyed)
                    continue;
                Bounds bounds = building.DestructionBounds;
                Vector3 closest = bounds.ClosestPoint(center);
                float distance = Vector3.Distance(center, closest);
                if (distance > radius)
                    continue;
                float falloff = 1f - Mathf.Clamp01(distance / radius);
                Vector3 direction = closest - center;
                if (direction.sqrMagnitude < 0.001f)
                    direction = Vector3.up;
                if (building.ApplyUrbanDamage(new UrbanDamageRequest(
                        closest,
                        -direction.normalized,
                        direction,
                        damage * falloff,
                        radius,
                        damage * falloff,
                        UrbanDamageKind.Explosion,
                        source)))
                {
                    affected++;
                }
            }
            for (int index = Decorations.Count - 1; index >= 0; index--)
            {
                UrbanDestructibleDecoration decoration = Decorations[index];
                if (decoration == null)
                {
                    Decorations.RemoveAt(index);
                    continue;
                }
                if (decoration.IsUrbanDestroyed)
                    continue;
                Bounds bounds = decoration.DestructionBounds;
                Vector3 closest = bounds.ClosestPoint(center);
                float distance = Vector3.Distance(center, closest);
                if (distance > radius)
                    continue;
                float falloff = 1f - Mathf.Clamp01(distance / radius);
                Vector3 direction = closest - center;
                if (direction.sqrMagnitude < 0.001f)
                    direction = Vector3.up;
                if (decoration.ApplyUrbanDamage(new UrbanDamageRequest(
                        closest,
                        -direction.normalized,
                        direction,
                        damage * falloff,
                        radius,
                        damage * falloff,
                        UrbanDamageKind.Explosion,
                        source)))
                {
                    affected++;
                }
            }
            for (int index = Bridges.Count - 1; index >= 0; index--)
            {
                UrbanDestructibleBridge bridge = Bridges[index];
                if (bridge == null)
                {
                    Bridges.RemoveAt(index);
                    continue;
                }
                if (bridge.IsUrbanDestroyed)
                    continue;
                Bounds bounds = bridge.DestructionBounds;
                Vector3 closest = bounds.ClosestPoint(center);
                float distance = Vector3.Distance(center, closest);
                if (distance > radius)
                    continue;
                float falloff = 1f - Mathf.Clamp01(distance / radius);
                Vector3 direction = closest - center;
                if (direction.sqrMagnitude < 0.001f)
                    direction = Vector3.up;
                if (bridge.ApplyUrbanDamage(new UrbanDamageRequest(
                        closest,
                        -direction.normalized,
                        direction,
                        damage * falloff,
                        radius,
                        damage * falloff,
                        UrbanDamageKind.Explosion,
                        source)))
                {
                    affected++;
                }
            }
            for (int index = Mathf.Min(
                     existingRuinSectionCount,
                     RuinSections.Count) - 1;
                 index >= 0;
                 index--)
            {
                UrbanDestructibleRuinSection section = RuinSections[index];
                if (section == null)
                {
                    RuinSections.RemoveAt(index);
                    continue;
                }
                if (section.IsUrbanDestroyed)
                    continue;
                Bounds bounds = section.DestructionBounds;
                Vector3 closest = bounds.ClosestPoint(center);
                float distance = Vector3.Distance(center, closest);
                if (distance > radius)
                    continue;
                float falloff = 1f - Mathf.Clamp01(distance / radius);
                Vector3 direction = closest - center;
                if (direction.sqrMagnitude < 0.001f)
                    direction = Vector3.up;
                if (section.ApplyUrbanDamage(new UrbanDamageRequest(
                        closest,
                        -direction.normalized,
                        direction,
                        damage * falloff,
                        radius,
                        damage * falloff,
                        UrbanDamageKind.Explosion,
                        source)))
                {
                    affected++;
                }
            }
            return affected;
        }

        internal static int ApplyStructuralImpact(
            Vector3 center,
            float radius,
            float damage,
            GameObject source)
        {
            if (radius <= 0.01f || damage <= 0f)
                return 0;
            int affected = 0;
            for (int index = Buildings.Count - 1; index >= 0; index--)
            {
                UrbanDestructibleBuilding building = Buildings[index];
                if (building == null)
                {
                    Buildings.RemoveAt(index);
                    continue;
                }
                if (building.IsUrbanDestroyed)
                    continue;
                Bounds bounds = building.DestructionBounds;
                float distance = Mathf.Sqrt(bounds.SqrDistance(center));
                if (distance > radius)
                    continue;
                float falloff = 1f - Mathf.Clamp01(distance / radius);
                Vector3 point = bounds.ClosestPoint(center);
                Vector3 direction = point - center;
                if (direction.sqrMagnitude < 0.001f)
                    direction = Vector3.down;
                if (building.ApplyUrbanDamage(new UrbanDamageRequest(
                        point,
                        -direction.normalized,
                        direction,
                        damage * Mathf.Lerp(0.35f, 1f, falloff),
                        radius,
                        damage,
                        UrbanDamageKind.HighSpeedImpact,
                        source)))
                {
                    affected++;
                }
            }
            return affected;
        }

        static UrbanDestructibleBuilding ResolveBuilding(Collider collider)
        {
            if (collider == null)
                return null;
            UrbanDestructibleBuilding building =
                collider.GetComponentInParent<UrbanDestructibleBuilding>();
            if (building != null)
                return building;
            UrbanStructuralColliderProxy proxy =
                collider.GetComponentInParent<UrbanStructuralColliderProxy>();
            return proxy != null ? proxy.Owner : null;
        }

        internal static void BreakDecorationsNear(Bounds collapseBounds)
        {
            collapseBounds.Expand(new Vector3(12f, 8f, 12f));
            for (int index = Decorations.Count - 1; index >= 0; index--)
            {
                UrbanDestructibleDecoration decoration = Decorations[index];
                if (decoration == null)
                {
                    Decorations.RemoveAt(index);
                    continue;
                }
                if (decoration.IsUrbanDestroyed ||
                    !collapseBounds.Intersects(decoration.DestructionBounds))
                {
                    continue;
                }
                Vector3 point = decoration.DestructionBounds.center;
                Vector3 direction = point - collapseBounds.center;
                decoration.ApplyUrbanDamage(new UrbanDamageRequest(
                    point,
                    direction.sqrMagnitude > 0.001f
                        ? -direction.normalized
                        : Vector3.up,
                    direction,
                    1000f,
                    12f,
                    1000f,
                    UrbanDamageKind.Explosion,
                    null));
            }

            collapseBounds.Expand(new Vector3(18f, 18f, 18f));
            for (int index = Bridges.Count - 1; index >= 0; index--)
            {
                UrbanDestructibleBridge bridge = Bridges[index];
                if (bridge == null)
                {
                    Bridges.RemoveAt(index);
                    continue;
                }
                if (bridge.IsUrbanDestroyed ||
                    !collapseBounds.Intersects(bridge.DestructionBounds))
                {
                    continue;
                }
                bridge.ForceBreakFromSupportLoss(collapseBounds.center);
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class UrbanDestructionCoordinator : MonoBehaviour
    {
        readonly Stack<UrbanDebrisPiece> debrisPool =
            new Stack<UrbanDebrisPiece>(96);
        readonly List<UrbanDebrisPiece> activeDebris =
            new List<UrbanDebrisPiece>(128);
        readonly Stack<UrbanDustBurst> dustPool =
            new Stack<UrbanDustBurst>(12);
        readonly List<UrbanDustBurst> activeDust =
            new List<UrbanDustBurst>(24);

        UrbanDestructionSettings settings = new UrbanDestructionSettings();
        Material concreteMaterial;
        Material structuralInteriorMaterial;
        Material scorchMaterial;
        Material shockwaveMaterial;
        Material dustMaterial;
        Texture2D dustTexture;
        Mesh dustVolumeMesh;
        int activePhysicalDebris;

        public int ActivePhysicalDebris => activePhysicalDebris;
        public int ActiveDebris => activeDebris.Count;
        public int MaximumPhysicalDebris => settings.maximumPhysicalDebris;
        internal int StructuralColumns => settings.structuralColumns;
        internal float TargetStructuralFloorHeight =>
            settings.targetStructuralFloorHeight;
        internal float NeighbourSupportTransfer =>
            settings.neighbourSupportTransfer;
        internal int MaximumVisibleStructuralHoles =>
            settings.maximumVisibleStructuralHoles;

        public void Configure(UrbanDestructionSettings target)
        {
            settings = (target ?? new UrbanDestructionSettings()).ValidatedCopy();
        }

        public float ScaleDamage(UrbanDamageKind kind, float damage)
        {
            switch (kind)
            {
                case UrbanDamageKind.StandardProjectile:
                    return Mathf.Min(10f,
                        damage * settings.normalProjectileDamageScale);
                case UrbanDamageKind.Explosion:
                    return damage * settings.explosionDamageScale;
                default:
                    return damage;
            }
        }

        public float ResolveDemolitionDamage(float maximumIntegrity, float damage)
        {
            return Mathf.Max(
                damage * 0.65f,
                maximumIntegrity * settings.demolitionIntegrityFraction);
        }

        public float EvaluateImpactDamage(
            float relativeSpeed,
            float impulseMagnitude,
            float maximumIntegrity)
        {
            if (relativeSpeed < settings.highSpeedImpactThreshold)
                return 0f;
            float excess = relativeSpeed - settings.highSpeedImpactThreshold;
            float raw = excess * 2.5f + impulseMagnitude * 0.015f;
            return Mathf.Clamp(
                raw * settings.impactDamageScale,
                0f,
                maximumIntegrity * 0.42f);
        }

        public void SpawnImpact(
            Bounds ownerBounds,
            Vector3 point,
            Vector3 normal,
            Vector3 incomingDirection,
            float radius,
            float severity,
            Material facadeMaterial,
            int stableSeed,
            bool demolition,
            Transform breachParent)
        {
            float clampedRadius = Mathf.Clamp(
                radius > 0.01f ? radius * 0.42f : 2.2f + severity * 2.8f,
                1.2f,
                Mathf.Min(13f, ownerBounds.extents.magnitude * 0.22f));
            CreateFacadeCracks(
                point,
                normal,
                clampedRadius * 0.92f,
                stableSeed,
                breachParent != null ? breachParent : transform);
            SpawnDust(point, normal, clampedRadius, demolition);
            SpawnShockwave(point, normal, clampedRadius, demolition);

            int fragments = demolition
                ? Mathf.Clamp(Mathf.RoundToInt(7f + severity * 5f), 7, 12)
                : Mathf.Clamp(Mathf.RoundToInt(2f + severity * 2f), 2, 4);
            var random = new System.Random(stableSeed);
            for (int index = 0; index < fragments; index++)
            {
                float size = Mathf.Lerp(
                    demolition ? 0.38f : 0.25f,
                    demolition ? 1.65f : 0.9f,
                    (float)random.NextDouble());
                Vector3 tangent = Vector3.Cross(
                    normal,
                    Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.86f
                        ? Vector3.right
                        : Vector3.up).normalized;
                Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
                Vector3 scatter = tangent * RandomSigned(random) +
                                  bitangent * RandomSigned(random) +
                                  normal * Mathf.Lerp(0.8f, 1.8f,
                                      (float)random.NextDouble());
                Vector3 velocity = scatter.normalized * Mathf.Lerp(
                    demolition ? 14f : 4f,
                    demolition ? 31f : 12f,
                    (float)random.NextDouble());
                if (incomingDirection.sqrMagnitude > 0.001f)
                    velocity += Vector3.ProjectOnPlane(
                        incomingDirection.normalized,
                        normal) * 1.4f;
                Material fragmentMaterial = facadeMaterial != null &&
                                            index % 3 != 1
                    ? facadeMaterial
                    : ResolveStructuralInteriorMaterial();
                SpawnDebris(
                    point + normal * 0.18f,
                    new Vector3(
                        size * Mathf.Lerp(0.45f, 1.2f, (float)random.NextDouble()),
                        size * Mathf.Lerp(0.35f, 1.0f, (float)random.NextDouble()),
                        size * Mathf.Lerp(0.35f, 1.1f, (float)random.NextDouble())),
                    velocity,
                    RandomRotation(random),
                    fragmentMaterial,
                    demolition && index < 4);
            }
        }

        public void SpawnRuinSectionDamage(
            UrbanDestructibleRuinSection section,
            in UrbanDamageRequest request,
            Material facadeMaterial,
            int stableSeed,
            int fractureStage)
        {
            if (section == null)
                return;
            Bounds bounds = section.DestructionBounds;
            Vector3 point = bounds.ClosestPoint(request.point);
            Vector3 normal = request.normal.sqrMagnitude > 0.001f
                ? request.normal.normalized
                : (point - bounds.center).normalized;
            if (normal.sqrMagnitude < 0.001f)
                normal = Vector3.up;

            SpawnImpact(
                bounds,
                point,
                normal,
                request.direction,
                request.radius,
                fractureStage > 0 ? 0.85f : 0.42f,
                facadeMaterial,
                stableSeed,
                request.kind == UrbanDamageKind.DemolitionProjectile ||
                request.kind == UrbanDamageKind.EnergyBlade,
                section.BreachVisualRoot);

            if (fractureStage <= 0)
                return;

            // Every persistent ruin stays damageable.  New physical children
            // shrink with the section that was actually hit, so the fracture
            // tree can continue until the pieces are too small to subdivide.
            // At that point hits still produce pooled impact debris without
            // multiplying persistent colliders forever.
            var random = new System.Random(stableSeed ^ fractureStage * 32452843);
            int count = section.FractureGeneration == 0 ? 3 : 2;
            float largestDimension = Mathf.Max(
                bounds.size.x,
                Mathf.Max(bounds.size.y, bounds.size.z));
            if (largestDimension <= 0.72f)
                return;
            count = UrbanDestructionWorld.AvailableRuinSectionSlots(count);
            if (count <= 0)
                return;

            Transform fractureParent = ResolveFractureParent(section);
            for (int index = 0; index < count; index++)
            {
                Vector3 childSize = new Vector3(
                    FracturedChildSize(bounds.size.x, random),
                    FracturedChildSize(bounds.size.y, random),
                    FracturedChildSize(bounds.size.z, random));
                Vector3 offset = new Vector3(
                    RandomSigned(random) * bounds.extents.x * 0.34f,
                    RandomSigned(random) * bounds.extents.y * 0.34f,
                    RandomSigned(random) * bounds.extents.z * 0.34f);
                Vector3 childPosition = Vector3.Lerp(
                    point,
                    bounds.center + offset,
                    0.58f) + normal * 0.12f;
                GameObject childObject = CreateRuinPiece(
                    fractureParent,
                    childPosition,
                    section.transform.rotation * RandomRotation(random),
                    childSize,
                    index == 0 ? facadeMaterial : ResolveConcreteMaterial(),
                    true,
                    "RecursiveFracture_可继续破裂_G" +
                    (section.FractureGeneration + 1) + "_" +
                    index.ToString("D2"));
                childObject.AddComponent<UrbanSliceVisual>();
                BoxCollider childCollider =
                    childObject.GetComponent<BoxCollider>();
                UrbanDestructibleRuinSection childSection =
                    childObject.AddComponent<UrbanDestructibleRuinSection>();
                childSection.Configure(
                    this,
                    index == 0 ? facadeMaterial : ResolveConcreteMaterial(),
                    stableSeed ^ index * 67867967,
                    "RuinChunk_G" + (section.FractureGeneration + 1),
                    childCollider,
                    section.FractureGeneration + 1);
            }
        }

        internal bool TrySliceRuinSection(
            UrbanDestructibleRuinSection section,
            in UrbanDamageRequest request,
            Material facadeMaterial,
            int stableSeed)
        {
            if (section == null || section.IsUrbanDestroyed ||
                section.FractureGeneration >= 5)
            {
                return false;
            }
            Bounds bounds = section.DestructionBounds;
            if (bounds.size.y < 2.4f ||
                UrbanDestructionWorld.AvailableRuinSectionSlots(2) < 1)
            {
                return false;
            }
            Renderer[] sources = section.GetOwnedSliceRenderers();
            if (sources.Length == 0)
                return false;

            float margin = Mathf.Clamp(bounds.size.y * 0.11f, 0.55f, 3.2f);
            if (bounds.min.y + margin >= bounds.max.y - margin)
                return false;
            float cutHeight = Mathf.Clamp(
                request.point.y,
                bounds.min.y + margin,
                bounds.max.y - margin);
            float fractureGap = Mathf.Clamp(
                bounds.size.y * 0.014f,
                0.55f,
                1.65f);

            Transform splitRoot = new GameObject(
                "RecursiveSlice_G" + (section.FractureGeneration + 1) +
                "_真正二分").transform;
            splitRoot.SetParent(section.transform, false);
            Transform lowerRoot = new GameObject(
                "SliceHalf_Lower_G" + (section.FractureGeneration + 1)).transform;
            lowerRoot.SetParent(splitRoot, false);
            Transform upperRoot = new GameObject(
                "SliceHalf_Upper_G" + (section.FractureGeneration + 1)).transform;
            upperRoot.SetParent(splitRoot, false);

            var ownedMeshes = new List<Mesh>(8);
            Plane lowerPlane = new Plane(
                Vector3.up,
                new Vector3(0f, cutHeight - fractureGap * 0.5f, 0f));
            Plane upperPlane = new Plane(
                Vector3.up,
                new Vector3(0f, cutHeight + fractureGap * 0.5f, 0f));
            int lowerCount = UrbanRuntimeMeshSlicer.CreateHalfRendererCopies(
                sources,
                lowerRoot,
                lowerPlane,
                false,
                ResolveStructuralInteriorMaterial(),
                ownedMeshes,
                "RecursiveLowerVisual_");
            int upperCount = UrbanRuntimeMeshSlicer.CreateHalfRendererCopies(
                sources,
                upperRoot,
                upperPlane,
                true,
                ResolveStructuralInteriorMaterial(),
                ownedMeshes,
                "RecursiveUpperVisual_");
            UrbanRuntimeResourceOwner owner =
                splitRoot.gameObject.AddComponent<UrbanRuntimeResourceOwner>();
            owner.Configure(Array.Empty<Material>(), ownedMeshes);
            if (lowerCount == 0 || upperCount == 0 ||
                !UrbanRuntimeMeshSlicer.TryGetLocalRendererBounds(
                    lowerRoot,
                    out Bounds lowerBounds) ||
                !UrbanRuntimeMeshSlicer.TryGetLocalRendererBounds(
                    upperRoot,
                    out Bounds upperBounds))
            {
                DestroyOwnedObject(splitRoot.gameObject);
                return false;
            }

            BoxCollider lowerCollider = CreateSliceSectionCollider(
                lowerRoot,
                lowerRoot.TransformPoint(lowerBounds.center),
                lowerBounds.size);
            BoxCollider upperCollider = CreateSliceSectionCollider(
                upperRoot,
                upperRoot.TransformPoint(upperBounds.center),
                upperBounds.size);

            // Retire the exact section that was hit. Its parent rigidbody and
            // toppling animation remain alive, while the two replacement
            // colliders become a compound body when appropriate.
            // The replacement visuals already live below the old section at
            // this point. Retire only the source snapshot; walking the whole
            // subtree here would also disable the two freshly sliced halves.
            section.RetireAfterSlice(sources);
            int nextGeneration = section.FractureGeneration + 1;
            UrbanDestructibleRuinSection lowerSection =
                lowerRoot.gameObject.AddComponent<UrbanDestructibleRuinSection>();
            lowerSection.Configure(
                this,
                facadeMaterial,
                stableSeed ^ 32452843,
                "RecursiveLowerHalf",
                lowerCollider,
                nextGeneration);
            UrbanDestructibleRuinSection upperSection =
                upperRoot.gameObject.AddComponent<UrbanDestructibleRuinSection>();
            upperSection.Configure(
                this,
                facadeMaterial,
                stableSeed ^ 49979687,
                "RecursiveUpperHalf",
                upperCollider,
                nextGeneration);

            UrbanDestructionFamily inheritedFamily =
                section.GetComponentInParent<UrbanDestructionFamily>();
            if (inheritedFamily != null && inheritedFamily.FamilyId != 0)
            {
                UrbanDestructionFamily detachedFamily =
                    upperRoot.gameObject.AddComponent<UrbanDestructionFamily>();
                detachedFamily.Configure(inheritedFamily.FamilyId);
            }

            Vector3 point = new Vector3(
                Mathf.Clamp(request.point.x, bounds.min.x, bounds.max.x),
                cutHeight,
                Mathf.Clamp(request.point.z, bounds.min.z, bounds.max.z));
            Vector3 normal = request.normal.sqrMagnitude > 0.001f
                ? request.normal.normalized
                : Vector3.up;
            SpawnImpact(
                bounds,
                point,
                normal,
                request.direction,
                Mathf.Clamp(request.radius * 0.35f, 2.5f, 9f),
                0.78f,
                facadeMaterial,
                stableSeed,
                true,
                splitRoot);
            SeparateUpperSlice(
                section,
                upperRoot,
                upperBounds,
                request.direction,
                request.normal,
                stableSeed);
            return true;
        }

        void SeparateUpperSlice(
            UrbanDestructibleRuinSection sourceSection,
            Transform upperRoot,
            Bounds upperLocalBounds,
            Vector3 bladeDirection,
            Vector3 hitNormal,
            int stableSeed)
        {
            if (upperRoot == null)
                return;
            Rigidbody inheritedBody = sourceSection != null
                ? sourceSection.GetComponentInParent<Rigidbody>()
                : null;
            Vector3 inheritedVelocity = inheritedBody != null &&
                                        !inheritedBody.isKinematic
                ? inheritedBody.velocity
                : Vector3.zero;
            Vector3 separationDirection = Vector3.ProjectOnPlane(
                bladeDirection,
                Vector3.up);
            if (separationDirection.sqrMagnitude < 0.001f)
            {
                separationDirection = Vector3.ProjectOnPlane(
                    -hitNormal,
                    Vector3.up);
            }
            if (separationDirection.sqrMagnitude < 0.001f)
                separationDirection = upperRoot.forward;
            separationDirection.Normalize();

            float horizontalSize = Mathf.Max(
                1f,
                Mathf.Min(upperLocalBounds.size.x, upperLocalBounds.size.z));
            float revealDistance = Mathf.Clamp(
                horizontalSize * 0.035f,
                0.75f,
                2.4f);

            // In play mode the severed half leaves the old compound body and
            // becomes independently collidable. The small immediate reveal is
            // intentional: without it two valid meshes occupy an almost
            // identical silhouette for the first frames and read as "not cut".
            if (Application.isPlaying)
            {
                upperRoot.SetParent(transform, true);
                upperRoot.position += separationDirection * revealDistance +
                                      Vector3.up * Mathf.Min(0.55f,
                                          revealDistance * 0.24f);
            }

            Rigidbody body = upperRoot.gameObject.AddComponent<Rigidbody>();
            body.mass = Mathf.Clamp(
                upperLocalBounds.size.x * upperLocalBounds.size.y *
                upperLocalBounds.size.z * 0.0022f,
                65f,
                1800f);
            body.drag = 0.055f;
            body.angularDrag = 0.12f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.maxAngularVelocity = 2.8f;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
            body.detectCollisions = true;
            body.isKinematic = !Application.isPlaying;
            body.useGravity = Application.isPlaying;
            if (!Application.isPlaying)
                return;

            float seedSign = (stableSeed & 1) == 0 ? 1f : -1f;
            body.velocity = inheritedVelocity +
                            separationDirection * 3.8f +
                            Vector3.up * 0.65f;
            body.angularVelocity =
                Vector3.Cross(Vector3.up, separationDirection) *
                (0.42f * seedSign) + separationDirection * 0.10f;
            body.WakeUp();
        }

        Transform ResolveFractureParent(
            UrbanDestructibleRuinSection section)
        {
            Rigidbody body = section == null
                ? null
                : section.GetComponentInParent<Rigidbody>();
            return body != null ? body.transform : transform;
        }

        static float FracturedChildSize(
            float parentSize,
            System.Random random)
        {
            parentSize = Mathf.Max(0.3f, parentSize);
            return Mathf.Clamp(
                parentSize * Mathf.Lerp(
                    0.24f,
                    0.42f,
                    (float)random.NextDouble()),
                0.3f,
                Mathf.Max(0.3f, parentSize * 0.48f));
        }

        public bool SpawnCollapse(
            UrbanDestructibleBuilding building,
            Vector3 impactPoint,
            Vector3 impactDirection,
            Material facadeMaterial,
            int stableSeed)
        {
            if (building == null)
                return false;
            Bounds bounds = building.DestructionBounds;
            Quaternion rotation = building.transform.rotation;
            Vector3 size = building.DesignSize;
            var random = new System.Random(stableSeed);
            Transform ruinRoot = new GameObject(
                "Ruin_" + building.StableId + "_残骸保持原占地").transform;
            ruinRoot.SetParent(transform, false);
            UrbanDestructionFamily family =
                ruinRoot.gameObject.AddComponent<UrbanDestructionFamily>();
            family.Configure(building.GetInstanceID());

            // Shell impacts remain local breaches.  A whole tower only topples
            // after its support system fails, so the structural hinge stays near
            // the foundation instead of slicing the facade at the shell height.
            float cutHeight = building.LastCollapseWasEnergyBlade
                ? Mathf.Clamp(
                    building.LastCollapseCutHeight,
                    bounds.min.y + size.y * 0.16f,
                    bounds.min.y + size.y * 0.84f)
                : Mathf.Clamp(
                    building.LastCollapseCutHeight,
                    bounds.min.y + size.y * 0.08f,
                    bounds.min.y + size.y * 0.24f);
            float fractureGap = Mathf.Clamp(size.y * 0.008f, 0.65f, 1.8f);
            float jaggedAmplitude = Mathf.Clamp(size.y * 0.018f, 1.2f, 3.8f);
            Vector3 fallDirection = Vector3.ProjectOnPlane(
                impactDirection,
                Vector3.up);
            if (fallDirection.sqrMagnitude < 0.001f)
            {
                fallDirection = Vector3.ProjectOnPlane(
                    impactPoint - bounds.center,
                    Vector3.up);
            }
            if (fallDirection.sqrMagnitude < 0.001f)
                fallDirection = rotation * Vector3.forward;
            fallDirection.Normalize();

            if (building.LastCollapseWasEnergyBlade)
            {
                CreateHitDrivenTopplingSections(
                    building,
                    ruinRoot,
                    bounds,
                    size,
                    cutHeight,
                    fractureGap,
                    jaggedAmplitude,
                    fallDirection,
                    facadeMaterial,
                    stableSeed);
            }
            else
            {
                CreateBuckledTopplingStructure(
                    building,
                    ruinRoot,
                    bounds,
                    size,
                    cutHeight,
                    fallDirection,
                    impactPoint,
                    facadeMaterial,
                    stableSeed);
            }

            float foundationHeight = Mathf.Clamp(size.y * 0.025f, 2.2f, 4.2f);
            CreateRuinPiece(
                ruinRoot,
                new Vector3(bounds.center.x, bounds.min.y + foundationHeight * 0.5f,
                    bounds.center.z),
                rotation,
                new Vector3(size.x * 0.72f, foundationHeight, size.z * 0.72f),
                ResolveConcreteMaterial(),
                true,
                "Foundation_承重基座");

            int stationaryPieces = Mathf.Clamp(
                Mathf.RoundToInt(size.y / 18f), 7, 12);
            float ruinHeight = Mathf.Clamp(size.y * 0.09f, 7f, 15f);
            for (int index = 0; index < stationaryPieces; index++)
            {
                float x01 = RandomSigned(random) * 0.72f;
                float z01 = RandomSigned(random) * 0.72f;
                float radial = Mathf.Clamp01(Mathf.Sqrt(x01 * x01 + z01 * z01));
                float localPileHeight = ruinHeight * Mathf.Lerp(1f, 0.42f, radial);
                float chunkHeight = Mathf.Lerp(1.4f, 4.6f,
                    (float)random.NextDouble());
                float chunkWidth = size.x * Mathf.Lerp(0.08f, 0.20f,
                    (float)random.NextDouble());
                float chunkDepth = size.z * Mathf.Lerp(0.08f, 0.20f,
                    (float)random.NextDouble());
                Vector3 local = new Vector3(
                    x01 * size.x * 0.42f,
                    -size.y * 0.5f + foundationHeight * 0.55f +
                    Mathf.Lerp(chunkHeight * 0.5f, localPileHeight,
                        (float)random.NextDouble()),
                    z01 * size.z * 0.42f);
                CreateRuinPiece(
                    ruinRoot,
                    bounds.center + rotation * local,
                    rotation * Quaternion.Euler(
                        RandomSigned(random) * 18f,
                        RandomSigned(random) * 28f,
                        RandomSigned(random) * 18f),
                    new Vector3(chunkWidth, chunkHeight, chunkDepth),
                    index % 5 == 1 ? facadeMaterial : ResolveConcreteMaterial(),
                    false,
                    "FractureCell_预裂残墙_" + index.ToString("D2"));
            }

            int fallingPieces = Mathf.Clamp(
                Mathf.RoundToInt(size.y / 22f), 5, 9);
            Vector3 outwardBias = -fallDirection;
            for (int index = 0; index < fallingPieces; index++)
            {
                float pieceScale = Mathf.Lerp(1.2f, 4.4f,
                    (float)random.NextDouble());
                Vector3 local = new Vector3(
                    RandomSigned(random) * size.x * 0.35f,
                    cutHeight - bounds.center.y +
                    RandomSigned(random) * jaggedAmplitude * 1.4f,
                    RandomSigned(random) * size.z * 0.35f);
                Vector3 outward = (rotation * new Vector3(
                    RandomSigned(random),
                    Mathf.Lerp(-0.18f, 0.38f, (float)random.NextDouble()),
                    RandomSigned(random))).normalized;
                Vector3 velocity = outward * Mathf.Lerp(8f, 22f,
                    (float)random.NextDouble());
                velocity += outwardBias * 6f;
                SpawnDebris(
                    bounds.center + rotation * local,
                    new Vector3(
                        pieceScale * Mathf.Lerp(0.55f, 1.35f, (float)random.NextDouble()),
                        pieceScale * Mathf.Lerp(0.35f, 0.9f, (float)random.NextDouble()),
                        pieceScale * Mathf.Lerp(0.45f, 1.15f, (float)random.NextDouble())),
                    velocity,
                    RandomRotation(random),
                    index % 6 == 0 ? facadeMaterial : ResolveConcreteMaterial(),
                    index < 5);
            }

            float collapseRadius = Mathf.Clamp(size.x + size.z, 14f, 42f);
            Vector3 fracturePoint = new Vector3(
                Mathf.Clamp(impactPoint.x, bounds.min.x, bounds.max.x),
                cutHeight,
                Mathf.Clamp(impactPoint.z, bounds.min.z, bounds.max.z));
            SpawnDust(
                fracturePoint,
                -fallDirection,
                collapseRadius * 0.62f,
                true);
            CreateDustVolume(
                fracturePoint,
                collapseRadius * 0.46f,
                true);
            CreateDustVolume(
                new Vector3(bounds.center.x,
                    cutHeight + Mathf.Min(size.y * 0.16f, 18f),
                    bounds.center.z),
                collapseRadius * 0.34f,
                true);
            SpawnShockwave(
                fracturePoint,
                -fallDirection,
                collapseRadius * 0.68f,
                true);
            return true;
        }

        internal int CreateDamageRendererCopies(
            Renderer[] sources,
            Transform parent,
            List<Renderer> targets,
            List<Material> ownedMaterials,
            List<Mesh> ownedMeshes)
        {
            if (sources == null || parent == null || targets == null)
                return 0;
            Shader damageShader = Shader.Find("UnityPlanet/UrbanBuildingCut");
            if (damageShader == null)
                return 0;

            var materialCache = new Dictionary<Material, Material>();
            int created = 0;
            for (int index = 0; index < sources.Length; index++)
            {
                Renderer source = sources[index];
                if (source == null)
                    continue;
                Mesh mesh = null;
                if (source is MeshRenderer)
                {
                    MeshFilter filter = source.GetComponent<MeshFilter>();
                    if (filter != null)
                        mesh = filter.sharedMesh;
                }
                else if (source is SkinnedMeshRenderer skinned)
                {
                    mesh = new Mesh
                    {
                        name = "UrbanDamageBaked_" + source.name
                    };
                    skinned.BakeMesh(mesh);
                    ownedMeshes?.Add(mesh);
                }
                if (mesh == null)
                    continue;

                var visual = new GameObject("LocalizedDamageVisual_" + source.name);
                visual.layer = source.gameObject.layer;
                visual.transform.SetParent(parent, true);
                visual.transform.position = source.transform.position;
                visual.transform.rotation = source.transform.rotation;
                visual.transform.localScale = source.transform.lossyScale;
                visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer target = visual.AddComponent<MeshRenderer>();
                target.shadowCastingMode = source.shadowCastingMode;
                target.receiveShadows = source.receiveShadows;
                target.lightProbeUsage = source.lightProbeUsage;
                target.reflectionProbeUsage = source.reflectionProbeUsage;

                Material[] sourceMaterials = source.sharedMaterials;
                if (sourceMaterials == null || sourceMaterials.Length == 0)
                    sourceMaterials = new[] { ResolveConcreteMaterial() };
                var targetMaterials = new Material[sourceMaterials.Length];
                for (int materialIndex = 0;
                     materialIndex < sourceMaterials.Length;
                     materialIndex++)
                {
                    Material sourceMaterial = sourceMaterials[materialIndex] ??
                                              ResolveConcreteMaterial();
                    if (!materialCache.TryGetValue(
                            sourceMaterial,
                            out Material damageMaterial))
                    {
                        damageMaterial = CreateCutFacadeMaterial(
                            sourceMaterial,
                            damageShader);
                        damageMaterial.name = "UrbanLocalizedDamage_" +
                                              sourceMaterial.name;
                        materialCache.Add(sourceMaterial, damageMaterial);
                        ownedMaterials?.Add(damageMaterial);
                    }
                    targetMaterials[materialIndex] = damageMaterial;
                }
                target.sharedMaterials = targetMaterials;
                targets.Add(target);
                created++;
            }
            return created;
        }

        internal Transform CreateDamageCavityInterior(
            Renderer[] sources,
            Bounds buildingBounds,
            IReadOnlyList<Bounds> holes,
            Transform parent,
            string stableId)
        {
            if (sources == null || sources.Length == 0 ||
                holes == null || holes.Count == 0 || parent == null)
            {
                return null;
            }

            Shader damageShader = Shader.Find("UnityPlanet/UrbanBuildingCut");
            if (damageShader == null)
                return null;

            var rootObject = new GameObject(
                "DamageCavityInterior_建筑自身内缩层_" + stableId);
            Transform root = rootObject.transform;
            root.SetParent(parent, true);
            root.position = Vector3.zero;
            root.rotation = Quaternion.identity;
            root.localScale = Vector3.one;

            float minimumHorizontal = Mathf.Max(
                1f,
                Mathf.Min(buildingBounds.size.x, buildingBounds.size.z));
            float horizontalInset = Mathf.Clamp(
                minimumHorizontal * 0.045f,
                0.65f,
                2.4f);
            float horizontalFactor = Mathf.Clamp01(
                (minimumHorizontal - horizontalInset * 2f) /
                minimumHorizontal);
            horizontalFactor = Mathf.Clamp(horizontalFactor, 0.72f, 0.965f);
            float verticalInset = Mathf.Clamp(
                buildingBounds.size.y * 0.008f,
                0.25f,
                0.9f);
            float verticalFactor = Mathf.Clamp(
                (buildingBounds.size.y - verticalInset * 2f) /
                Mathf.Max(1f, buildingBounds.size.y),
                0.965f,
                0.995f);
            Vector3 shrink = new Vector3(
                horizontalFactor,
                verticalFactor,
                horizontalFactor);

            var materialCache = new Dictionary<Material, Material>();
            var ownedMaterials = new List<Material>(8);
            var ownedMeshes = new List<Mesh>(2);
            int created = 0;
            for (int index = 0; index < sources.Length; index++)
            {
                Renderer source = sources[index];
                if (source == null)
                    continue;
                Mesh mesh = null;
                if (source is MeshRenderer)
                {
                    MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
                    if (sourceFilter != null)
                        mesh = sourceFilter.sharedMesh;
                }
                else if (source is SkinnedMeshRenderer skinned)
                {
                    mesh = new Mesh
                    {
                        name = "UrbanDamageInnerBaked_" + source.name
                    };
                    skinned.BakeMesh(mesh);
                    ownedMeshes.Add(mesh);
                }
                if (mesh == null)
                    continue;

                var innerObject = new GameObject(
                    "DamageCavityOriginalShell_" + index.ToString("D2") +
                    "_" + source.name);
                innerObject.layer = source.gameObject.layer;
                Transform inner = innerObject.transform;
                inner.SetParent(root, true);
                inner.position = buildingBounds.center + Vector3.Scale(
                    source.transform.position - buildingBounds.center,
                    shrink);
                inner.rotation = source.transform.rotation;
                inner.localScale = Vector3.Scale(
                    source.transform.lossyScale,
                    shrink);
                innerObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer target = innerObject.AddComponent<MeshRenderer>();
                target.shadowCastingMode = source.shadowCastingMode;
                target.receiveShadows = source.receiveShadows;
                target.lightProbeUsage = source.lightProbeUsage;
                target.reflectionProbeUsage = source.reflectionProbeUsage;

                Material[] sourceMaterials = source.sharedMaterials;
                if (sourceMaterials == null || sourceMaterials.Length == 0)
                    sourceMaterials = new[] { ResolveStructuralInteriorMaterial() };
                var innerMaterials = new Material[sourceMaterials.Length];
                for (int materialIndex = 0;
                     materialIndex < sourceMaterials.Length;
                     materialIndex++)
                {
                    Material sourceMaterial = sourceMaterials[materialIndex] ??
                                              ResolveStructuralInteriorMaterial();
                    if (!materialCache.TryGetValue(
                            sourceMaterial,
                            out Material innerMaterial))
                    {
                        innerMaterial = CreateCutFacadeMaterial(
                            sourceMaterial,
                            damageShader);
                        innerMaterial.name = "UrbanInnerShell_" +
                                             sourceMaterial.name;
                        DarkenSourceBuildingMaterial(innerMaterial);
                        materialCache.Add(sourceMaterial, innerMaterial);
                        ownedMaterials.Add(innerMaterial);
                    }
                    innerMaterials[materialIndex] = innerMaterial;
                }
                target.sharedMaterials = innerMaterials;
                ApplyStructuralRegionMask(target, holes);
                created++;
            }

            if (created > 0)
            {
                UrbanRuntimeResourceOwner owner =
                    rootObject.AddComponent<UrbanRuntimeResourceOwner>();
                owner.Configure(ownedMaterials, ownedMeshes);
                return root;
            }
            DestroyOwnedObject(rootObject);
            return null;
        }

        static void DarkenSourceBuildingMaterial(Material material)
        {
            if (material == null)
                return;
            if (material.HasProperty("_Color"))
            {
                Color source = material.GetColor("_Color");
                float luminance = source.r * 0.2126f +
                                  source.g * 0.7152f +
                                  source.b * 0.0722f;
                float graphite = Mathf.Clamp(
                    0.055f + luminance * 0.085f,
                    0.06f,
                    0.14f);
                material.SetColor(
                    "_Color",
                    new Color(
                        graphite * 0.82f,
                        graphite * 0.92f,
                        graphite * 1.06f,
                        source.a));
            }
            if (material.HasProperty("_EmissionColor"))
                material.SetColor(
                    "_EmissionColor",
                    material.GetColor("_EmissionColor") * 0.10f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0.08f);
        }

        internal UrbanDestructibleRuinSection SpawnUnsupportedCluster(
            UrbanDestructibleBuilding building,
            IReadOnlyList<Bounds> cellBounds,
            Vector3 impactDirection,
            Material facadeMaterial,
            int stableSeed)
        {
            if (building == null || cellBounds == null || cellBounds.Count == 0)
                return null;

            List<Bounds> structuralRegions = BuildStructuralRegions(
                cellBounds,
                8);
            Bounds clusterBounds = structuralRegions[0];
            for (int index = 1; index < structuralRegions.Count; index++)
                clusterBounds.Encapsulate(structuralRegions[index]);
            var clusterObject = new GameObject(
                "UnsupportedCluster_局部失稳_" + building.StableId + "_" +
                stableSeed);
            clusterObject.transform.SetParent(transform, true);
            clusterObject.transform.position = clusterBounds.center;
            clusterObject.transform.rotation = Quaternion.identity;

            Transform visualRoot = new GameObject("ConnectedStructuralCells").transform;
            visualRoot.SetParent(clusterObject.transform, false);
            var clusterRenderers = new List<Renderer>(8);
            var ownedMaterials = new List<Material>(8);
            var ownedMeshes = new List<Mesh>(4);
            int copied = CreateDamageRendererCopies(
                building.SourceRenderers,
                visualRoot,
                clusterRenderers,
                ownedMaterials,
                ownedMeshes);
            for (int index = 0; index < clusterRenderers.Count; index++)
            {
                Renderer renderer = clusterRenderers[index];
                renderer.name = "DetachedOriginalFacade_" + index.ToString("D2");
                ApplyStructuralRegionMask(renderer, structuralRegions);
            }

            Material interior = ResolveStructuralInteriorMaterial();
            for (int index = 0; index < structuralRegions.Count; index++)
            {
                Bounds region = structuralRegions[index];
                float minimumWidth = Mathf.Min(region.size.x, region.size.z);
                float beam = Mathf.Clamp(minimumWidth * 0.075f, 0.32f, 1.15f);
                CreateRuinPiece(
                    visualRoot,
                    region.center,
                    Quaternion.identity,
                    new Vector3(beam, region.size.y * 0.86f, beam),
                    interior,
                    false,
                    "ExposedColumn_" + index.ToString("D2"));
                CreateRuinPiece(
                    visualRoot,
                    new Vector3(
                        region.center.x,
                        region.min.y + Mathf.Max(0.22f, beam * 0.45f),
                        region.center.z),
                    Quaternion.identity,
                    new Vector3(
                        region.size.x * 0.72f,
                        Mathf.Max(0.24f, beam * 0.42f),
                        region.size.z * 0.72f),
                    interior,
                    false,
                    "FracturedFloorEdge_" + index.ToString("D2"));
            }
            if (copied > 0)
            {
                UrbanRuntimeResourceOwner owner =
                    clusterObject.AddComponent<UrbanRuntimeResourceOwner>();
                owner.Configure(ownedMaterials, ownedMeshes);
            }

            BoxCollider primaryCollider = null;
            for (int index = 0; index < structuralRegions.Count; index++)
            {
                Bounds region = structuralRegions[index];
                BoxCollider collider = clusterObject.AddComponent<BoxCollider>();
                collider.center = clusterObject.transform.InverseTransformPoint(
                    region.center);
                collider.size = Vector3.Max(
                    Vector3.one * 0.45f,
                    Vector3.Scale(
                        region.size,
                        new Vector3(0.88f, 0.92f, 0.88f)));
                if (primaryCollider == null)
                    primaryCollider = collider;
            }
            Rigidbody body = clusterObject.AddComponent<Rigidbody>();
            body.mass = Mathf.Clamp(
                clusterBounds.size.x * clusterBounds.size.y *
                clusterBounds.size.z * 0.008f,
                80f,
                4500f);
            body.drag = 0.035f;
            body.angularDrag = 0.10f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.maxAngularVelocity = 3.5f;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
            body.isKinematic = !Application.isPlaying;
            body.useGravity = Application.isPlaying;
            Vector3 outward = Vector3.ProjectOnPlane(
                impactDirection,
                Vector3.up);
            if (outward.sqrMagnitude < 0.001f)
                outward = clusterBounds.center - building.DestructionBounds.center;
            if (outward.sqrMagnitude < 0.001f)
                outward = Vector3.forward;
            outward.Normalize();
            Vector3 initialVelocity = outward * 2.1f + Vector3.down * 3.2f;
            Vector3 initialAngularVelocity =
                Vector3.Cross(Vector3.up, outward) * 0.38f;
            if (Application.isPlaying)
            {
                body.velocity = initialVelocity;
                body.angularVelocity = initialAngularVelocity;
            }

            UrbanDestructibleRuinSection section =
                clusterObject.AddComponent<UrbanDestructibleRuinSection>();
            section.Configure(
                this,
                facadeMaterial,
                stableSeed,
                "UnsupportedStructuralCluster",
                primaryCollider,
                0);
            UrbanStructuralClusterMotion motion =
                clusterObject.AddComponent<UrbanStructuralClusterMotion>();
            motion.Configure(
                body,
                clusterBounds,
                building.gameObject,
                initialVelocity,
                initialAngularVelocity);
            return section;
        }

        internal List<Bounds> BuildStructuralRegions(
            IReadOnlyList<Bounds> source,
            int maximumRegions)
        {
            var regions = new List<Bounds>(source != null ? source.Count : 0);
            if (source == null || source.Count == 0)
                return regions;
            for (int index = 0; index < source.Count; index++)
                regions.Add(source[index]);

            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int first = 0; first < regions.Count && !changed; first++)
                for (int second = first + 1; second < regions.Count; second++)
                {
                    Bounds a = regions[first];
                    Bounds b = regions[second];
                    float toleranceX = Mathf.Min(a.size.x, b.size.x) * 0.08f;
                    float toleranceZ = Mathf.Min(a.size.z, b.size.z) * 0.08f;
                    bool sameColumn =
                        Mathf.Abs(a.center.x - b.center.x) <= toleranceX &&
                        Mathf.Abs(a.center.z - b.center.z) <= toleranceZ;
                    float verticalGap = Mathf.Max(
                        0f,
                        Mathf.Max(a.min.y, b.min.y) -
                        Mathf.Min(a.max.y, b.max.y));
                    if (!sameColumn || verticalGap > 0.12f)
                        continue;
                    a.Encapsulate(b);
                    regions[first] = a;
                    regions.RemoveAt(second);
                    changed = true;
                    break;
                }
            }

            maximumRegions = Mathf.Clamp(maximumRegions, 1, 8);
            while (regions.Count > maximumRegions)
            {
                int bestFirst = 0;
                int bestSecond = 1;
                float bestCost = float.PositiveInfinity;
                for (int first = 0; first < regions.Count; first++)
                for (int second = first + 1; second < regions.Count; second++)
                {
                    Bounds combined = regions[first];
                    combined.Encapsulate(regions[second]);
                    float cost = BoundsVolume(combined) -
                                 BoundsVolume(regions[first]) -
                                 BoundsVolume(regions[second]);
                    if (cost >= bestCost)
                        continue;
                    bestCost = cost;
                    bestFirst = first;
                    bestSecond = second;
                }
                Bounds merged = regions[bestFirst];
                merged.Encapsulate(regions[bestSecond]);
                regions[bestFirst] = merged;
                regions.RemoveAt(bestSecond);
            }
            return regions;
        }

        static float BoundsVolume(Bounds bounds)
        {
            return Mathf.Max(0.001f,
                bounds.size.x * bounds.size.y * bounds.size.z);
        }

        static void ApplyStructuralRegionMask(
            Renderer renderer,
            IReadOnlyList<Bounds> worldRegions)
        {
            if (renderer == null || worldRegions == null)
                return;
            var properties = new MaterialPropertyBlock();
            properties.SetFloat("_DamageMode", 2f);
            properties.SetFloat("_DamageLocalSpace", 1f);
            int count = Mathf.Min(8, worldRegions.Count);
            properties.SetFloat("_DamageHoleCount", count);
            for (int index = 0; index < 8; index++)
            {
                Bounds local = index < count
                    ? ToLocalBounds(renderer.transform, worldRegions[index])
                    : new Bounds(Vector3.zero, Vector3.zero);
                properties.SetVector(
                    "_DamageHole" + index,
                    new Vector4(local.center.x, local.center.y, local.center.z, 1f));
                properties.SetVector(
                    "_DamageExtent" + index,
                    new Vector4(
                        local.extents.x * 0.98f,
                        local.extents.y * 0.98f,
                        local.extents.z * 0.98f,
                        0f));
            }
            renderer.SetPropertyBlock(properties);
        }

        static Bounds ToLocalBounds(Transform target, Bounds worldBounds)
        {
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;
            Vector3 first = target.InverseTransformPoint(min);
            Bounds local = new Bounds(first, Vector3.zero);
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                local.Encapsulate(target.InverseTransformPoint(new Vector3(
                    x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z)));
            }
            return local;
        }

        internal void SpawnStructuralFailureWarning(
            Bounds bounds,
            Vector3 direction)
        {
            Vector3 outward = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (outward.sqrMagnitude < 0.001f)
                outward = Vector3.forward;
            outward.Normalize();
            Vector3 failurePoint = new Vector3(
                bounds.center.x,
                bounds.min.y + Mathf.Min(1.2f, bounds.size.y * 0.08f),
                bounds.center.z);
            float radius = Mathf.Clamp(
                Mathf.Min(bounds.size.x, bounds.size.z) * 0.34f,
                2.2f,
                7.5f);
            SpawnDust(failurePoint, -outward, radius, false);
            CreateDustVolume(failurePoint, radius * 0.52f, false);
        }

        void CreateBuckledTopplingStructure(
            UrbanDestructibleBuilding building,
            Transform ruinRoot,
            Bounds bounds,
            Vector3 size,
            float hingeHeight,
            Vector3 fallDirection,
            Vector3 impactPoint,
            Material facadeMaterial,
            int stableSeed)
        {
            var ownedMaterials = new List<Material>(0);
            var ownedMeshes = new List<Mesh>(2);
            float hingeRadius = Mathf.Abs(fallDirection.x) * bounds.extents.x +
                                Mathf.Abs(fallDirection.z) * bounds.extents.z;
            float foundationY = Mathf.Clamp(
                hingeHeight,
                bounds.min.y + size.y * 0.025f,
                bounds.min.y + size.y * 0.12f);
            Vector3 hingePoint = new Vector3(
                bounds.center.x,
                foundationY,
                bounds.center.z) + fallDirection * hingeRadius * 0.78f;
            Transform fallingRoot = new GameObject(
                "BuckledTower_基础失稳整楼屈曲_H" +
                Mathf.RoundToInt(foundationY)).transform;
            fallingRoot.SetParent(ruinRoot, true);
            fallingRoot.position = hingePoint;
            fallingRoot.rotation = Quaternion.identity;

            int wholeVisualCount = CreateWholeRendererCopies(
                building.SourceRenderers,
                fallingRoot,
                ownedMeshes);
            if (wholeVisualCount == 0)
            {
                GameObject fallback = CreateRuinPiece(
                    fallingRoot,
                    bounds.center,
                    building.transform.rotation,
                    size * 0.94f,
                    facadeMaterial,
                    false,
                    "BuckledWholeFacadeFallback_原材质兼容保底");
                fallback.AddComponent<UrbanSliceVisual>();
            }

            // An inset structural core prevents an imported facade from reading
            // as a hollow card when it leans. It stays inside the authored shell.
            CreateRuinPiece(
                fallingRoot,
                bounds.center,
                building.transform.rotation,
                new Vector3(size.x * 0.54f, size.y * 0.94f, size.z * 0.54f),
                ResolveConcreteMaterial(),
                false,
                "BuckledInteriorCore_屈曲楼体内部承重核");

            Vector3 impactNormal = impactPoint - bounds.center;
            impactNormal.y = 0f;
            if (impactNormal.sqrMagnitude < 0.001f)
                impactNormal = -fallDirection;
            impactNormal.Normalize();
            Vector3 facadePoint = bounds.ClosestPoint(impactPoint);
            SpawnImpact(
                bounds,
                facadePoint,
                impactNormal,
                fallDirection,
                Mathf.Clamp(Mathf.Min(size.x, size.z) * 0.24f, 4f, 12f),
                0.82f,
                facadeMaterial,
                stableSeed ^ 86028121,
                false,
                fallingRoot);

            // Three unequal crushed supports show *why* the tower falls. They
            // cluster on the failed edge instead of drawing a horizontal cut.
            var random = new System.Random(stableSeed ^ 15485863);
            Vector3 side = Vector3.Cross(Vector3.up, fallDirection).normalized;
            for (int index = 0; index < 3; index++)
            {
                float lateral = (index - 1) * Mathf.Min(size.x, size.z) * 0.18f;
                float supportHeight = Mathf.Lerp(1.8f, 4.6f,
                    (float)random.NextDouble());
                Vector3 supportPoint = new Vector3(
                    bounds.center.x,
                    bounds.min.y + supportHeight * 0.5f,
                    bounds.center.z) +
                    fallDirection * hingeRadius * 0.62f +
                    side * lateral;
                CreateRuinPiece(
                    ruinRoot,
                    supportPoint,
                    building.transform.rotation * Quaternion.Euler(
                        RandomSigned(random) * 12f,
                        RandomSigned(random) * 18f,
                        RandomSigned(random) * 12f),
                    new Vector3(
                        Mathf.Clamp(size.x * 0.16f, 2.4f, 8f),
                        supportHeight,
                        Mathf.Clamp(size.z * 0.16f, 2.4f, 8f)),
                    index == 1 ? ResolveScorchMaterial() : ResolveConcreteMaterial(),
                    index == 0,
                    "CrushedSupport_局部承重失效_" + index.ToString("D2"));
            }

            BoxCollider towerCollider =
                fallingRoot.gameObject.AddComponent<BoxCollider>();
            towerCollider.center = fallingRoot.InverseTransformPoint(bounds.center);
            towerCollider.size = new Vector3(
                size.x * 0.84f,
                size.y * 0.96f,
                size.z * 0.84f);
            Rigidbody body = fallingRoot.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.detectCollisions = true;
            body.mass = Mathf.Clamp(
                size.x * size.z * size.y * 0.0025f,
                120f,
                2200f);
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;

            UrbanTopplingSection toppling =
                fallingRoot.gameObject.AddComponent<UrbanTopplingSection>();
            toppling.Configure(
                towerCollider,
                body,
                Vector3.Cross(Vector3.up, fallDirection).normalized,
                fallDirection,
                foundationY,
                76f,
                Mathf.Lerp(2.25f, 3.25f,
                    Mathf.InverseLerp(35f, 190f, size.y)));

            UrbanDestructibleRuinSection fallenSection =
                fallingRoot.gameObject.AddComponent<UrbanDestructibleRuinSection>();
            fallenSection.Configure(
                this,
                facadeMaterial,
                stableSeed ^ 130363,
                "FallenTower_Buckled",
                towerCollider,
                0);

            UrbanRuntimeResourceOwner owner =
                ruinRoot.gameObject.AddComponent<UrbanRuntimeResourceOwner>();
            owner.Configure(ownedMaterials, ownedMeshes);
        }

        int CreateWholeRendererCopies(
            Renderer[] sources,
            Transform parent,
            List<Mesh> ownedMeshes)
        {
            if (sources == null)
                return 0;
            int created = 0;
            for (int index = 0; index < sources.Length; index++)
            {
                Renderer source = sources[index];
                if (source == null)
                    continue;
                Mesh mesh = null;
                if (source is MeshRenderer)
                {
                    MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
                    if (sourceFilter != null)
                        mesh = sourceFilter.sharedMesh;
                }
                else if (source is SkinnedMeshRenderer skinned)
                {
                    mesh = new Mesh
                    {
                        name = "UrbanBuckledBaked_" + source.name
                    };
                    skinned.BakeMesh(mesh);
                    ownedMeshes.Add(mesh);
                }
                if (mesh == null)
                    continue;

                var visual = new GameObject("BuckledWholeVisual_" + source.name);
                visual.layer = source.gameObject.layer;
                visual.transform.SetParent(parent, false);
                visual.transform.position = source.transform.position;
                visual.transform.rotation = source.transform.rotation;
                visual.transform.localScale = source.transform.lossyScale;
                visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer target = visual.AddComponent<MeshRenderer>();
                target.shadowCastingMode = source.shadowCastingMode;
                target.receiveShadows = source.receiveShadows;
                target.lightProbeUsage = source.lightProbeUsage;
                target.reflectionProbeUsage = source.reflectionProbeUsage;
                Material[] sourceMaterials = source.sharedMaterials;
                target.sharedMaterials = sourceMaterials != null &&
                                         sourceMaterials.Length > 0
                    ? sourceMaterials
                    : new[] { ResolveConcreteMaterial() };
                visual.AddComponent<UrbanSliceVisual>();
                created++;
            }
            return created;
        }

        void CreateHitDrivenTopplingSections(
            UrbanDestructibleBuilding building,
            Transform ruinRoot,
            Bounds bounds,
            Vector3 size,
            float cutHeight,
            float fractureGap,
            float jaggedAmplitude,
            Vector3 fallDirection,
            Material facadeMaterial,
            int stableSeed)
        {
            var ownedMaterials = new List<Material>(12);
            var ownedMeshes = new List<Mesh>(2);
            var cutMaterialCache = new Dictionary<Material, Material>();

            Transform lowerRoot = new GameObject(
                "LowerSection_命中高度以下保留楼体").transform;
            lowerRoot.SetParent(ruinRoot, false);

            float hingeRadius = Mathf.Abs(fallDirection.x) * bounds.extents.x +
                                Mathf.Abs(fallDirection.z) * bounds.extents.z;
            Vector3 hingePoint = new Vector3(
                bounds.center.x,
                cutHeight,
                bounds.center.z) + fallDirection * hingeRadius * 0.82f;
            Transform fallingRoot = new GameObject(
                "TopplingSection_绕命中切口倾倒_H" +
                Mathf.RoundToInt(cutHeight)).transform;
            fallingRoot.SetParent(ruinRoot, true);
            fallingRoot.position = hingePoint;
            fallingRoot.rotation = Quaternion.identity;

            int lowerVisualCount = CreateClippedRendererCopies(
                building.SourceRenderers,
                lowerRoot,
                cutHeight,
                fractureGap,
                jaggedAmplitude,
                stableSeed,
                false,
                cutMaterialCache,
                ownedMaterials,
                ownedMeshes);
            int upperVisualCount = CreateClippedRendererCopies(
                building.SourceRenderers,
                fallingRoot,
                cutHeight,
                fractureGap,
                jaggedAmplitude,
                stableSeed,
                true,
                cutMaterialCache,
                ownedMaterials,
                ownedMeshes);

            float lowerHeight = Mathf.Max(
                2f,
                cutHeight - bounds.min.y - fractureGap * 0.5f);
            float upperHeight = Mathf.Max(
                2f,
                bounds.max.y - cutHeight - fractureGap * 0.5f);
            Vector3 lowerCenter = new Vector3(
                bounds.center.x,
                bounds.min.y + lowerHeight * 0.5f,
                bounds.center.z);
            Vector3 upperCenter = new Vector3(
                bounds.center.x,
                cutHeight + fractureGap * 0.5f + upperHeight * 0.5f,
                bounds.center.z);

            // Never hide the authored building unless a visible replacement is
            // guaranteed.  Unsupported/custom shaders fall back to facade-clad
            // structural proxies, rather than producing an invisible ruin.
            if (lowerVisualCount == 0)
            {
                GameObject fallback = CreateRuinPiece(
                    lowerRoot,
                    lowerCenter,
                    building.transform.rotation,
                    new Vector3(size.x * 0.94f, lowerHeight, size.z * 0.94f),
                    facadeMaterial,
                    false,
                    "LowerFacadeFallback_材质兼容保底");
                fallback.AddComponent<UrbanSliceVisual>();
            }
            if (upperVisualCount == 0)
            {
                GameObject fallback = CreateRuinPiece(
                    fallingRoot,
                    upperCenter,
                    building.transform.rotation,
                    new Vector3(size.x * 0.92f, upperHeight, size.z * 0.92f),
                    facadeMaterial,
                    false,
                    "UpperFacadeFallback_材质兼容保底");
                fallback.AddComponent<UrbanSliceVisual>();
            }

            // Real clipped meshes carry their own cap submesh.  Do not insert a
            // generic concrete box: it changes the silhouette and is exactly
            // what made repeated cuts look like accumulating cubes.
            BoxCollider lowerCollider = CreateSliceSectionCollider(
                lowerRoot,
                lowerCenter,
                new Vector3(size.x * 0.82f, lowerHeight, size.z * 0.82f));
            BoxCollider topCollider = CreateSliceSectionCollider(
                fallingRoot,
                upperCenter,
                new Vector3(size.x * 0.80f, upperHeight, size.z * 0.80f));
            Rigidbody body = fallingRoot.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.detectCollisions = true;
            body.mass = Mathf.Clamp(
                size.x * size.z * upperHeight * 0.0025f,
                80f,
                1800f);
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            UrbanTopplingSection toppling =
                fallingRoot.gameObject.AddComponent<UrbanTopplingSection>();
            toppling.Configure(
                topCollider,
                body,
                Vector3.Cross(Vector3.up, fallDirection).normalized,
                fallDirection,
                cutHeight,
                82f,
                Mathf.Lerp(1.8f, 2.65f,
                    Mathf.InverseLerp(25f, 180f, upperHeight)));

            UrbanDestructibleRuinSection lowerSection =
                lowerRoot.gameObject.AddComponent<UrbanDestructibleRuinSection>();
            lowerSection.Configure(
                this,
                facadeMaterial,
                stableSeed ^ 104729,
                "LowerRuin",
                lowerCollider);
            UrbanDestructibleRuinSection fallenSection =
                fallingRoot.gameObject.AddComponent<UrbanDestructibleRuinSection>();
            fallenSection.Configure(
                this,
                facadeMaterial,
                stableSeed ^ 130363,
                "FallenTower",
                topCollider);

            UrbanRuntimeResourceOwner owner =
                ruinRoot.gameObject.AddComponent<UrbanRuntimeResourceOwner>();
            owner.Configure(ownedMaterials, ownedMeshes);
        }

        int CreateClippedRendererCopies(
            Renderer[] sources,
            Transform parent,
            float cutHeight,
            float fractureGap,
            float jaggedAmplitude,
            int stableSeed,
            bool keepAbove,
            Dictionary<Material, Material> materialCache,
            List<Material> ownedMaterials,
            List<Mesh> ownedMeshes)
        {
            if (sources == null || parent == null)
                return 0;
            float planeHeight = cutHeight +
                                (keepAbove ? 1f : -1f) *
                                fractureGap * 0.5f;
            var plane = new Plane(
                Vector3.up,
                new Vector3(0f, planeHeight, 0f));
            return UrbanRuntimeMeshSlicer.CreateHalfRendererCopies(
                sources,
                parent,
                plane,
                keepAbove,
                ResolveStructuralInteriorMaterial(),
                ownedMeshes,
                keepAbove ? "UpperCutVisual_" : "LowerCutVisual_");
        }

        static BoxCollider CreateSliceSectionCollider(
            Transform sectionRoot,
            Vector3 fallbackWorldCenter,
            Vector3 fallbackWorldSize)
        {
            var collider = sectionRoot.gameObject.AddComponent<BoxCollider>();
            if (UrbanRuntimeMeshSlicer.TryGetLocalRendererBounds(
                    sectionRoot,
                    out Bounds localBounds))
            {
                collider.center = localBounds.center;
                collider.size = Vector3.Max(
                    localBounds.size,
                    Vector3.one * 0.3f);
                return collider;
            }
            collider.center = sectionRoot.InverseTransformPoint(
                fallbackWorldCenter);
            Vector3 scale = sectionRoot.lossyScale;
            collider.size = new Vector3(
                Mathf.Abs(scale.x) > 0.0001f
                    ? fallbackWorldSize.x / Mathf.Abs(scale.x)
                    : fallbackWorldSize.x,
                Mathf.Abs(scale.y) > 0.0001f
                    ? fallbackWorldSize.y / Mathf.Abs(scale.y)
                    : fallbackWorldSize.y,
                Mathf.Abs(scale.z) > 0.0001f
                    ? fallbackWorldSize.z / Mathf.Abs(scale.z)
                    : fallbackWorldSize.z);
            return collider;
        }

        static Material CreateCutFacadeMaterial(
            Material source,
            Shader cutShader)
        {
            var material = new Material(cutShader)
            {
                name = "UrbanCutFacade_" +
                       (source != null ? source.name : "Fallback")
            };
            CopyTexture(source, material, "_MainTex", "_MainTex", "_BaseMap");
            CopyTexture(source, material, "_BumpMap", "_BumpMap", "_NormalMap");
            CopyTexture(source, material, "_MetallicGlossMap", "_MetallicGlossMap");
            CopyTexture(source, material, "_OcclusionMap", "_OcclusionMap");
            CopyTexture(source, material, "_EmissionMap", "_EmissionMap");
            CopyColor(source, material, "_Color", "_Color", "_BaseColor");
            CopyColor(source, material, "_EmissionColor", "_EmissionColor");
            CopyFloat(source, material, "_Metallic", "_Metallic");
            CopyFloat(source, material, "_Glossiness", "_Glossiness", "_Smoothness");
            CopyFloat(source, material, "_GlossMapScale", "_GlossMapScale");
            CopyFloat(source, material, "_BumpScale", "_BumpScale");
            CopyFloat(source, material, "_OcclusionStrength", "_OcclusionStrength");
            if (material.GetTexture("_BumpMap") != null)
                material.EnableKeyword("_NORMALMAP");
            if (material.GetTexture("_EmissionMap") != null ||
                material.GetColor("_EmissionColor").maxColorComponent > 0.001f)
            {
                material.EnableKeyword("_EMISSION");
            }
            return material;
        }

        static void CopyTexture(
            Material source,
            Material target,
            string targetProperty,
            params string[] sourceProperties)
        {
            if (source == null || target == null ||
                !target.HasProperty(targetProperty))
            {
                return;
            }
            for (int index = 0; index < sourceProperties.Length; index++)
            {
                string property = sourceProperties[index];
                if (!source.HasProperty(property))
                    continue;
                Texture texture = source.GetTexture(property);
                if (texture == null)
                    continue;
                target.SetTexture(targetProperty, texture);
                target.SetTextureScale(targetProperty,
                    source.GetTextureScale(property));
                target.SetTextureOffset(targetProperty,
                    source.GetTextureOffset(property));
                return;
            }
        }

        static void CopyColor(
            Material source,
            Material target,
            string targetProperty,
            params string[] sourceProperties)
        {
            if (source == null || target == null ||
                !target.HasProperty(targetProperty))
            {
                return;
            }
            for (int index = 0; index < sourceProperties.Length; index++)
            {
                string property = sourceProperties[index];
                if (!source.HasProperty(property))
                    continue;
                target.SetColor(targetProperty, source.GetColor(property));
                return;
            }
        }

        static void CopyFloat(
            Material source,
            Material target,
            string targetProperty,
            params string[] sourceProperties)
        {
            if (source == null || target == null ||
                !target.HasProperty(targetProperty))
            {
                return;
            }
            for (int index = 0; index < sourceProperties.Length; index++)
            {
                string property = sourceProperties[index];
                if (!source.HasProperty(property))
                    continue;
                target.SetFloat(targetProperty, source.GetFloat(property));
                return;
            }
        }

        public void SpawnDecorationBreak(
            Bounds bounds,
            Vector3 direction,
            Material material,
            int stableSeed)
        {
            var random = new System.Random(stableSeed);
            int count = Mathf.Clamp(
                Mathf.RoundToInt(bounds.extents.magnitude * 0.28f), 3, 6);
            for (int index = 0; index < count; index++)
            {
                float scale = Mathf.Clamp(
                    bounds.extents.magnitude * 0.12f,
                    0.25f,
                    2.4f);
                Vector3 velocity = (direction.sqrMagnitude > 0.001f
                    ? direction.normalized
                    : Vector3.up) * Mathf.Lerp(3f, 11f,
                    (float)random.NextDouble());
                velocity += new Vector3(
                    RandomSigned(random) * 4f,
                    Mathf.Lerp(2f, 8f, (float)random.NextDouble()),
                    RandomSigned(random) * 4f);
                SpawnDebris(
                    bounds.center,
                    Vector3.one * scale,
                    velocity,
                    RandomRotation(random),
                    material,
                    false);
            }
            SpawnDust(bounds.center, Vector3.up, 2.5f, false);
        }

        void SpawnDebris(
            Vector3 position,
            Vector3 size,
            Vector3 velocity,
            Quaternion rotation,
            Material material,
            bool requestPhysical)
        {
            if (activeDebris.Count >= settings.maximumVisualDebris)
                RecycleOldestDebris();
            UrbanDebrisPiece piece = debrisPool.Count > 0
                ? debrisPool.Pop()
                : UrbanDebrisPiece.Create(this);
            bool physical = requestPhysical &&
                            activePhysicalDebris < settings.maximumPhysicalDebris;
            if (physical)
                activePhysicalDebris++;
            activeDebris.Add(piece);
            piece.Launch(
                position,
                size,
                rotation,
                velocity,
                material != null ? material : ResolveConcreteMaterial(),
                physical,
                settings.physicalCollisionSeconds,
                settings.debrisLifetime);
        }

        void RecycleOldestDebris()
        {
            if (activeDebris.Count == 0)
                return;
            ReleaseDebris(activeDebris[0]);
        }

        internal void ReleaseDebris(UrbanDebrisPiece piece)
        {
            if (piece == null || !activeDebris.Remove(piece))
                return;
            if (piece.WasPhysical)
                activePhysicalDebris = Mathf.Max(0, activePhysicalDebris - 1);
            piece.PrepareForPool();
            debrisPool.Push(piece);
        }

        void SpawnDust(
            Vector3 position,
            Vector3 normal,
            float radius,
            bool heavy)
        {
            UrbanDustBurst burst = dustPool.Count > 0
                ? dustPool.Pop()
                : UrbanDustBurst.Create(this, ResolveDustMaterial());
            activeDust.Add(burst);
            burst.Play(position, normal, radius, heavy);
            CreateDustVolume(position, radius, heavy);
        }

        void CreateDustVolume(Vector3 position, float radius, bool heavy)
        {
            var volume = new GameObject(heavy
                ? "UrbanDustVolume_重型坍塌粉尘"
                : "UrbanDustVolume_受击粉尘");
            volume.transform.SetParent(transform, true);
            volume.transform.position = position;
            MeshFilter filter = volume.AddComponent<MeshFilter>();
            if (dustVolumeMesh == null)
                dustVolumeMesh = CreateDustVolumeMesh();
            filter.sharedMesh = dustVolumeMesh;
            MeshRenderer renderer = volume.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ResolveDustMaterial();
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 12;
            UrbanTransientVisual visual =
                volume.AddComponent<UrbanTransientVisual>();
            visual.Configure(
                heavy ? 2.8f : 1.55f,
                true,
                this,
                Mathf.Max(1.2f, radius * (heavy ? 1.18f : 0.86f)));
        }

        static Mesh CreateDustVolumeMesh()
        {
            Vector3[] centers =
            {
                new Vector3(0f, 0.10f, 0f),
                new Vector3(-0.34f, 0.04f, 0.10f),
                new Vector3(0.31f, 0.02f, -0.14f),
                new Vector3(0.12f, 0.31f, 0.18f),
                new Vector3(-0.16f, 0.36f, -0.20f),
                new Vector3(0.42f, 0.23f, 0.22f),
                new Vector3(-0.43f, 0.20f, -0.06f)
            };
            float[] sizes = { 0.34f, 0.27f, 0.30f, 0.29f, 0.25f, 0.22f, 0.24f };
            Vector3[] axesU = { Vector3.right, Vector3.forward, Vector3.right };
            Vector3[] axesV = { Vector3.up, Vector3.up, Vector3.forward };
            int planeCount = centers.Length * 3;
            var vertices = new Vector3[planeCount * 4];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[planeCount * 12];
            int plane = 0;
            for (int puff = 0; puff < centers.Length; puff++)
            for (int facing = 0; facing < 3; facing++, plane++)
            {
                int vertex = plane * 4;
                float size = sizes[puff];
                Vector3 u = axesU[facing] * size;
                Vector3 v = axesV[facing] * size;
                vertices[vertex] = centers[puff] - u - v;
                vertices[vertex + 1] = centers[puff] - u + v;
                vertices[vertex + 2] = centers[puff] + u + v;
                vertices[vertex + 3] = centers[puff] + u - v;
                uv[vertex] = Vector2.zero;
                uv[vertex + 1] = Vector2.up;
                uv[vertex + 2] = Vector2.one;
                uv[vertex + 3] = Vector2.right;
                int triangle = plane * 12;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 1;
                triangles[triangle + 2] = vertex + 2;
                triangles[triangle + 3] = vertex;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
                triangles[triangle + 6] = vertex + 2;
                triangles[triangle + 7] = vertex + 1;
                triangles[triangle + 8] = vertex;
                triangles[triangle + 9] = vertex + 3;
                triangles[triangle + 10] = vertex + 2;
                triangles[triangle + 11] = vertex;
            }
            var mesh = new Mesh
            {
                name = "UrbanDustVolume_CrossedSoftPuffs",
                vertices = vertices,
                triangles = triangles,
                uv = uv
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        internal void ReleaseDust(UrbanDustBurst burst)
        {
            if (burst == null || !activeDust.Remove(burst))
                return;
            burst.PrepareForPool();
            dustPool.Push(burst);
        }

        void CreateFacadeCracks(
            Vector3 point,
            Vector3 normal,
            float radius,
            int stableSeed,
            Transform parent)
        {
            var root = new GameObject("UrbanBreachCracks_Radial_" + stableSeed);
            root.transform.SetParent(parent != null ? parent : transform, true);
            var random = new System.Random(stableSeed ^ 83492791);
            Vector3 tangent = Vector3.Cross(
                normal,
                Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.86f
                    ? Vector3.right
                    : Vector3.up).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
            const int CrackCount = 6;
            for (int index = 0; index < CrackCount; index++)
            {
                float angle = index * Mathf.PI * 2f / CrackCount +
                              RandomSigned(random) * 0.18f;
                Vector3 radial = tangent * Mathf.Cos(angle) +
                                 bitangent * Mathf.Sin(angle);
                Vector3 sideways = -tangent * Mathf.Sin(angle) +
                                   bitangent * Mathf.Cos(angle);
                float length = radius * Mathf.Lerp(
                    1.06f,
                    1.32f,
                    (float)random.NextDouble());
                var crackObject = new GameObject(
                    "Crack_" + index.ToString("D2"));
                crackObject.transform.SetParent(root.transform, true);
                LineRenderer line = crackObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 5;
                line.sharedMaterial = ResolveScorchMaterial();
                line.widthMultiplier = Mathf.Lerp(
                    0.035f,
                    0.085f,
                    (float)random.NextDouble());
                line.numCornerVertices = 2;
                line.numCapVertices = 2;
                Vector3 surfaceOffset = normal * 0.115f;
                line.SetPosition(
                    0,
                    point + surfaceOffset + radial * radius * 0.70f);
                for (int pointIndex = 1; pointIndex < line.positionCount; pointIndex++)
                {
                    float t = pointIndex / (float)(line.positionCount - 1);
                    line.SetPosition(
                        pointIndex,
                        point + surfaceOffset +
                        radial * Mathf.Lerp(radius * 0.70f, length, t) +
                        sideways * RandomSigned(random) * radius *
                        Mathf.Lerp(0.035f, 0.12f, t));
                }
            }
            UrbanTransientVisual lifetime =
                root.AddComponent<UrbanTransientVisual>();
            lifetime.Configure(18f, false, this);
        }

        void SpawnShockwave(
            Vector3 point,
            Vector3 normal,
            float radius,
            bool strong)
        {
            var ringObject = new GameObject("UrbanShockwave_冲击波");
            ringObject.transform.SetParent(transform, true);
            ringObject.transform.position = point + normal * 0.15f;
            ringObject.transform.rotation = Quaternion.FromToRotation(
                Vector3.up,
                normal);
            LineRenderer line = ringObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 40;
            line.sharedMaterial = ResolveShockwaveMaterial();
            line.widthMultiplier = strong ? 0.42f : 0.20f;
            Color color = strong
                ? new Color(0.82f, 0.25f, 0.055f, 0.38f)
                : new Color(0.78f, 0.34f, 0.09f, 0.28f);
            line.startColor = color;
            line.endColor = color;
            for (int index = 0; index < line.positionCount; index++)
            {
                float angle = index * Mathf.PI * 2f / line.positionCount;
                line.SetPosition(index, new Vector3(
                    Mathf.Cos(angle),
                    0f,
                    Mathf.Sin(angle)));
            }
            UrbanTransientVisual visual =
                ringObject.AddComponent<UrbanTransientVisual>();
            visual.Configure(strong ? 0.72f : 0.38f, true, this, radius);
        }

        GameObject CreateRuinPiece(
            Transform parent,
            Vector3 position,
            Quaternion rotation,
            Vector3 size,
            Material material,
            bool collidable,
            string objectName)
        {
            var piece = new GameObject();
            piece.name = objectName;
            piece.transform.SetParent(parent, true);
            piece.transform.position = position;
            piece.transform.rotation = rotation;
            piece.transform.localScale = new Vector3(
                Mathf.Max(0.3f, size.x),
                Mathf.Max(0.3f, size.y),
                Mathf.Max(0.3f, size.z));
            MeshFilter filter = piece.AddComponent<MeshFilter>();
            Mesh mesh = UrbanDebrisPiece.CreateFractureMesh(
                objectName.GetHashCode() ^ piece.GetInstanceID());
            filter.sharedMesh = mesh;
            UrbanRuntimeMeshOwner meshOwner =
                piece.AddComponent<UrbanRuntimeMeshOwner>();
            meshOwner.Configure(mesh);
            MeshRenderer renderer = piece.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material != null
                ? material
                : ResolveConcreteMaterial();
            if (collidable)
                piece.AddComponent<BoxCollider>();
            return piece;
        }

        Material ResolveConcreteMaterial()
        {
            if (concreteMaterial != null)
                return concreteMaterial;
            concreteMaterial = CreateRuntimeMaterial(
                "UrbanRuin_InteriorConcrete",
                new Color(0.46f, 0.42f, 0.37f, 1f),
                false,
                0f);
            return concreteMaterial;
        }

        Material ResolveStructuralInteriorMaterial()
        {
            if (structuralInteriorMaterial != null)
                return structuralInteriorMaterial;
            structuralInteriorMaterial = CreateRuntimeMaterial(
                "UrbanRuin_ExposedStructuralSteel",
                new Color(0.105f, 0.125f, 0.145f, 1f),
                false,
                0f);
            if (structuralInteriorMaterial.HasProperty("_Metallic"))
                structuralInteriorMaterial.SetFloat("_Metallic", 0.48f);
            if (structuralInteriorMaterial.HasProperty("_Glossiness"))
                structuralInteriorMaterial.SetFloat("_Glossiness", 0.22f);
            return structuralInteriorMaterial;
        }

        Material ResolveScorchMaterial()
        {
            if (scorchMaterial != null)
                return scorchMaterial;
            scorchMaterial = CreateRuntimeMaterial(
                "UrbanRuin_Scorch",
                new Color(0.018f, 0.012f, 0.009f, 1f),
                false,
                0f);
            if (scorchMaterial.HasProperty("_Cull"))
                scorchMaterial.SetFloat("_Cull", 0f);
            return scorchMaterial;
        }

        Material ResolveShockwaveMaterial()
        {
            if (shockwaveMaterial != null)
                return shockwaveMaterial;
            shockwaveMaterial = CreateRuntimeMaterial(
                "UrbanRuin_Shockwave",
                new Color(0.72f, 0.16f, 0.025f, 0.30f),
                true,
                0.32f);
            return shockwaveMaterial;
        }

        Material ResolveDustMaterial()
        {
            if (dustMaterial != null)
                return dustMaterial;
            Shader shader = Shader.Find("Legacy Shaders/Transparent/Diffuse") ??
                            Shader.Find("Unlit/Transparent") ??
                            Shader.Find("Legacy Shaders/Particles/Alpha Blended") ??
                            Shader.Find("Particles/Standard Unlit") ??
                            Shader.Find("Unlit/Color");
            dustMaterial = new Material(shader)
            {
                name = "UrbanRuin_Dust"
            };
            if (dustMaterial.HasProperty("_Color"))
                dustMaterial.SetColor(
                    "_Color",
                    new Color(0.38f, 0.34f, 0.29f, 0.42f));
            if (dustMaterial.HasProperty("_TintColor"))
                dustMaterial.SetColor(
                    "_TintColor",
                    new Color(0.38f, 0.34f, 0.29f, 0.42f));
            dustTexture = CreateSoftParticleTexture();
            if (dustMaterial.HasProperty("_MainTex"))
                dustMaterial.SetTexture("_MainTex", dustTexture);
            if (dustMaterial.HasProperty("_Mode"))
                dustMaterial.SetFloat("_Mode", 2f);
            if (dustMaterial.HasProperty("_SrcBlend"))
                dustMaterial.SetInt(
                    "_SrcBlend",
                    (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (dustMaterial.HasProperty("_DstBlend"))
                dustMaterial.SetInt(
                    "_DstBlend",
                    (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (dustMaterial.HasProperty("_ZWrite"))
                dustMaterial.SetInt("_ZWrite", 0);
            dustMaterial.DisableKeyword("_ALPHATEST_ON");
            dustMaterial.EnableKeyword("_ALPHABLEND_ON");
            dustMaterial.renderQueue = 3000;
            return dustMaterial;
        }

        static Texture2D CreateSoftParticleTexture()
        {
            const int Size = 64;
            var texture = new Texture2D(
                Size,
                Size,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "UrbanDust_SoftRadial",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var colors = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float nx = (x + 0.5f) / Size * 2f - 1f;
                float ny = (y + 0.5f) / Size * 2f - 1f;
                float distance = Mathf.Sqrt(nx * nx + ny * ny);
                float alpha = Mathf.Clamp01(1f - distance);
                alpha = alpha * alpha * (3f - 2f * alpha);
                byte value = (byte)Mathf.RoundToInt(alpha * 255f);
                colors[y * Size + x] = new Color32(255, 255, 255, value);
            }
            texture.SetPixels32(colors);
            texture.Apply(false, true);
            return texture;
        }

        static Material CreateRuntimeMaterial(
            string materialName,
            Color color,
            bool transparent,
            float emission)
        {
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = materialName };
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_EmissionColor") && emission > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * emission);
            }
            if (transparent && material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = 3000;
            }
            if (transparent && material.HasProperty("_Cull"))
                material.SetInt("_Cull", 0);
            return material;
        }

        public void DebugAdvanceForAudit(float seconds)
        {
            for (int index = activeDebris.Count - 1; index >= 0; index--)
                activeDebris[index].DebugAdvance(seconds);
            for (int index = activeDust.Count - 1; index >= 0; index--)
                activeDust[index].DebugAdvance(seconds);
            UrbanTransientVisual[] transientVisuals =
                GetComponentsInChildren<UrbanTransientVisual>(true);
            for (int index = 0; index < transientVisuals.Length; index++)
                transientVisuals[index].DebugAdvance(seconds);
            UrbanTopplingSection[] topplingSections =
                GetComponentsInChildren<UrbanTopplingSection>(true);
            for (int index = 0; index < topplingSections.Length; index++)
                topplingSections[index].DebugAdvance(seconds);
            UrbanStructuralClusterMotion[] structuralClusters =
                GetComponentsInChildren<UrbanStructuralClusterMotion>(true);
            for (int index = 0; index < structuralClusters.Length; index++)
                structuralClusters[index].DebugAdvance(seconds);
        }

        static float RandomSigned(System.Random random)
        {
            return (float)random.NextDouble() * 2f - 1f;
        }

        static Quaternion RandomRotation(System.Random random)
        {
            return Quaternion.Euler(
                (float)random.NextDouble() * 360f,
                (float)random.NextDouble() * 360f,
                (float)random.NextDouble() * 360f);
        }

        static void DestroyOwnedObject(UnityEngine.Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }

        void OnDestroy()
        {
            DestroyOwnedObject(concreteMaterial);
            DestroyOwnedObject(structuralInteriorMaterial);
            DestroyOwnedObject(scorchMaterial);
            DestroyOwnedObject(shockwaveMaterial);
            DestroyOwnedObject(dustMaterial);
            DestroyOwnedObject(dustTexture);
            DestroyOwnedObject(dustVolumeMesh);
        }
    }

    // Retained temporarily as a migration reference. The active implementation
    // lives in UrbanStructuralBuildingRuntime.cs and uses localized support cells.
#if false
    [DisallowMultipleComponent]
    public sealed class UrbanDestructibleBuilding : MonoBehaviour, IUrbanDestructible
    {
        readonly float[] support = new float[3];
        Renderer[] originalRenderers = Array.Empty<Renderer>();
        Collider[] originalColliders = Array.Empty<Collider>();
        UrbanDestructionCoordinator coordinator;
        Vector3 designSize;
        string stableId = string.Empty;
        float maximumIntegrity;
        float integrity;
        float lastImpactTime = -100f;
        float accumulatedChipDamage;
        float lastCollapseCutHeight;
        Vector3 lastCollapseImpactPoint;
        Vector3 lastCollapseDirection;
        int breachCount;
        int stableHash;
        bool configured;
        bool collapsed;
        bool lastCollapseWasEnergyBlade;
        Material facadeMaterial;
        Transform breachVisualRoot;

        public Bounds DestructionBounds
        {
            get
            {
                Bounds bounds = new Bounds(
                    transform.position + transform.up * (designSize.y * 0.5f),
                    Vector3.zero);
                bool initialized = false;
                for (int index = 0; index < originalRenderers.Length; index++)
                {
                    Renderer renderer = originalRenderers[index];
                    if (renderer == null)
                        continue;
                    if (!initialized)
                    {
                        bounds = renderer.bounds;
                        initialized = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }
                if (!initialized)
                    bounds.size = designSize;
                return bounds;
            }
        }

        public bool IsUrbanDestroyed => collapsed;
        public bool IsCollapsed => collapsed;
        public float Integrity01 => maximumIntegrity <= 0.01f
            ? 0f
            : Mathf.Clamp01(integrity / maximumIntegrity);
        public Vector3 DesignSize => designSize;
        public string StableId => stableId;
        public int BreachCount => breachCount;
        public float LastCollapseCutHeight => lastCollapseCutHeight;
        public Vector3 LastCollapseImpactPoint => lastCollapseImpactPoint;
        public Vector3 LastCollapseDirection => lastCollapseDirection;
        public bool LastCollapseWasEnergyBlade => lastCollapseWasEnergyBlade;
        internal Renderer[] SourceRenderers => originalRenderers;

        public void Configure(
            AirCombatBuildingLot lot,
            UrbanDestructionCoordinator targetCoordinator)
        {
            if (lot == null)
                return;
            coordinator = targetCoordinator;
            stableId = lot.stableId ?? string.Empty;
            stableHash = StableHash(stableId);
            designSize = new Vector3(
                Mathf.Max(1f, lot.size.x),
                Mathf.Max(1f, lot.size.y),
                Mathf.Max(1f, lot.size.z));
            float footprint = designSize.x * designSize.z;
            maximumIntegrity = Mathf.Clamp(
                90f + designSize.y * 1.05f + footprint * 0.018f,
                140f,
                620f);
            integrity = maximumIntegrity;
            // Foundation, frame and crown do not have equal structural roles.
            // The base is deliberately the hardest support to remove; upper
            // shell hits mostly create local damage instead of a clean slice.
            support[0] = maximumIntegrity * 0.85f;
            support[1] = maximumIntegrity * 0.68f;
            support[2] = maximumIntegrity * 0.52f;
            originalRenderers = GetComponentsInChildren<Renderer>(true);
            originalColliders = GetComponentsInChildren<Collider>(true);
            facadeMaterial = ResolveFacadeMaterial(originalRenderers);
            configured = true;
            UrbanDestructionWorld.Register(this);
        }

        void OnEnable()
        {
            if (configured)
                UrbanDestructionWorld.Register(this);
        }

        void OnDisable()
        {
            UrbanDestructionWorld.Unregister(this);
        }

        public bool ApplyUrbanDamage(in UrbanDamageRequest request)
        {
            if (!configured || collapsed || coordinator == null ||
                request.damage <= 0f)
            {
                return false;
            }
            float applied = coordinator.ScaleDamage(request.kind, request.damage);
            if (request.kind == UrbanDamageKind.DemolitionProjectile)
            {
                applied = coordinator.ResolveDemolitionDamage(
                    maximumIntegrity,
                    request.damage);
            }
            if (applied <= 0.001f)
                return false;

            Bounds bounds = DestructionBounds;
            if (request.kind == UrbanDamageKind.EnergyBlade)
            {
                breachCount++;
                coordinator.SpawnImpact(
                    bounds,
                    ClampPointToFacade(bounds, request.point, request.normal),
                    request.normal,
                    request.direction,
                    Mathf.Clamp(request.radius * 0.22f, 3f, 11f),
                    1f,
                    ResolveFacadeMaterialAt(request.point),
                    stableHash ^ breachCount * 73856093,
                    true,
                    EnsureBreachVisualRoot());
                Collapse(request, false, true);
                return true;
            }
            int band = Mathf.Clamp(
                Mathf.FloorToInt(
                    Mathf.InverseLerp(bounds.min.y, bounds.max.y, request.point.y) * 3f),
                0,
                2);
            float height01 = Mathf.Clamp01(
                Mathf.InverseLerp(bounds.min.y, bounds.max.y, request.point.y));
            float supportMultiplier;
            if (request.kind == UrbanDamageKind.DemolitionProjectile)
            {
                supportMultiplier = band == 0 ? 0.62f :
                                    band == 1 ? 0.32f : 0.18f;
            }
            else
            {
                supportMultiplier = band == 0 ? 0.28f :
                                    band == 1 ? 0.18f : 0.10f;
            }
            support[band] = Mathf.Max(
                0f,
                support[band] - applied * supportMultiplier);
            if (request.kind == UrbanDamageKind.DemolitionProjectile && band > 0)
            {
                support[band - 1] = Mathf.Max(
                    0f,
                    support[band - 1] - applied * (0.055f + height01 * 0.025f));
            }
            float integrityScale = request.kind == UrbanDamageKind.DemolitionProjectile
                ? 0.48f
                : 1f;
            integrity = Mathf.Max(0f, integrity - applied * integrityScale);

            bool strongVisual = request.kind == UrbanDamageKind.DemolitionProjectile ||
                                request.kind == UrbanDamageKind.HighSpeedImpact ||
                                (request.kind == UrbanDamageKind.Explosion && applied >= 18f);
            accumulatedChipDamage += applied;
            if (strongVisual || accumulatedChipDamage >= maximumIntegrity * 0.09f)
            {
                accumulatedChipDamage = 0f;
                breachCount++;
                coordinator.SpawnImpact(
                    bounds,
                    ClampPointToFacade(bounds, request.point, request.normal),
                    request.normal,
                    request.direction,
                    request.radius,
                    Mathf.Clamp01(applied / (maximumIntegrity * 0.48f)),
                    ResolveFacadeMaterialAt(request.point),
                    stableHash ^ breachCount * 73856093,
                    request.kind == UrbanDamageKind.DemolitionProjectile,
                    EnsureBreachVisualRoot());
            }

            bool unsupportedBase = support[0] <= 0.01f &&
                                   integrity <= maximumIntegrity * 0.68f;
            bool progressiveFrameFailure = integrity <= maximumIntegrity * 0.12f;
            if (unsupportedBase || progressiveFrameFailure)
                Collapse(request, unsupportedBase, false);
            return true;
        }

        void Collapse(
            in UrbanDamageRequest request,
            bool foundationFailure,
            bool energyBladeCut)
        {
            if (collapsed)
                return;
            Bounds collapseBounds = DestructionBounds;
            lastCollapseImpactPoint = request.point;
            if (energyBladeCut)
            {
                lastCollapseCutHeight = Mathf.Clamp(
                    request.point.y,
                    collapseBounds.min.y + collapseBounds.size.y * 0.16f,
                    collapseBounds.min.y + collapseBounds.size.y * 0.84f);
            }
            else
            {
                float hingeFraction = foundationFailure
                    ? Mathf.Lerp(0.10f, 0.15f,
                        Mathf.Abs((stableHash % 997) / 996f))
                    : Mathf.Lerp(0.16f, 0.22f,
                        Mathf.Abs((stableHash % 991) / 990f));
                lastCollapseCutHeight = Mathf.Lerp(
                    collapseBounds.min.y,
                    collapseBounds.max.y,
                    hingeFraction);
            }
            lastCollapseWasEnergyBlade = energyBladeCut;
            lastCollapseDirection = Vector3.ProjectOnPlane(
                request.direction,
                Vector3.up).normalized;
            if (lastCollapseDirection.sqrMagnitude < 0.001f)
                lastCollapseDirection = transform.forward;
            Material collapseFacade = ResolveFacadeMaterialAt(request.point);
            bool replacementReady = coordinator.SpawnCollapse(
                this,
                request.point,
                request.direction,
                collapseFacade,
                stableHash ^ breachCount * 19349663);
            if (!replacementReady)
                return;

            collapsed = true;
            for (int index = 0; index < originalRenderers.Length; index++)
                if (originalRenderers[index] != null)
                    originalRenderers[index].enabled = false;
            for (int index = 0; index < originalColliders.Length; index++)
                if (originalColliders[index] != null)
                    originalColliders[index].enabled = false;
            DestroyBreachVisuals();
            UrbanDestructionWorld.BreakDecorationsNear(DestructionBounds);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!configured || collapsed || coordinator == null ||
                collision == null || collision.contactCount == 0)
            {
                return;
            }
            if (Time.unscaledTime - lastImpactTime < 0.35f)
                return;
            float speed = collision.relativeVelocity.magnitude;
            float damage = coordinator.EvaluateImpactDamage(
                speed,
                collision.impulse.magnitude,
                maximumIntegrity);
            if (damage <= 0.01f)
                return;
            lastImpactTime = Time.unscaledTime;
            ContactPoint contact = collision.GetContact(0);
            ApplyUrbanDamage(new UrbanDamageRequest(
                contact.point,
                contact.normal,
                collision.relativeVelocity,
                damage,
                Mathf.Clamp(speed * 0.12f, 2f, 11f),
                collision.impulse.magnitude,
                UrbanDamageKind.HighSpeedImpact,
                collision.gameObject));
        }

        public void DebugApplyDemolition(Vector3 point, Vector3 normal)
        {
            ApplyUrbanDamage(new UrbanDamageRequest(
                point,
                normal,
                -normal,
                150f,
                20f,
                150f,
                UrbanDamageKind.DemolitionProjectile,
                null));
        }

        static Vector3 ClampPointToFacade(
            Bounds bounds,
            Vector3 point,
            Vector3 normal)
        {
            Vector3 direction = normal.sqrMagnitude > 0.001f
                ? normal.normalized
                : (point - bounds.center).normalized;
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector3.forward;

            Vector3 origin = bounds.ClosestPoint(point);

            // Bounds.IntersectRay may report the entry face behind an origin that is
            // already inside the AABB.  Compute the nearest positive exit explicitly
            // so impact visuals always remain on the camera/weapon-facing facade.
            float distance = float.PositiveInfinity;
            ResolvePositiveExit(
                origin.x,
                bounds.min.x,
                bounds.max.x,
                direction.x,
                ref distance);
            ResolvePositiveExit(
                origin.y,
                bounds.min.y,
                bounds.max.y,
                direction.y,
                ref distance);
            ResolvePositiveExit(
                origin.z,
                bounds.min.z,
                bounds.max.z,
                direction.z,
                ref distance);
            return float.IsInfinity(distance)
                ? bounds.ClosestPoint(point)
                : origin + direction * distance;
        }

        static void ResolvePositiveExit(
            float center,
            float minimum,
            float maximum,
            float direction,
            ref float nearest)
        {
            if (Mathf.Abs(direction) <= 0.0001f)
                return;
            float boundary = direction > 0f ? maximum : minimum;
            float distance = (boundary - center) / direction;
            if (distance >= 0f && distance < nearest)
                nearest = distance;
        }

        static Material ResolveFacadeMaterial(Renderer[] renderers)
        {
            if (renderers == null)
                return null;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer != null && renderer.sharedMaterial != null)
                    return renderer.sharedMaterial;
            }
            return null;
        }

        Material ResolveFacadeMaterialAt(Vector3 point)
        {
            Material nearestMaterial = facadeMaterial;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < originalRenderers.Length; index++)
            {
                Renderer renderer = originalRenderers[index];
                if (renderer == null || renderer.sharedMaterial == null)
                    continue;
                Vector3 closest = renderer.bounds.ClosestPoint(point);
                float distance = (closest - point).sqrMagnitude;
                if (distance >= nearestDistance)
                    continue;
                nearestDistance = distance;
                nearestMaterial = renderer.sharedMaterial;
            }
            return nearestMaterial;
        }

        Transform EnsureBreachVisualRoot()
        {
            if (breachVisualRoot != null)
                return breachVisualRoot;
            var root = new GameObject(
                "UrbanBreachVisuals_" + stableId + "_随楼体清理");
            breachVisualRoot = root.transform;
            breachVisualRoot.SetParent(transform, false);
            return breachVisualRoot;
        }

        void DestroyBreachVisuals()
        {
            if (breachVisualRoot == null)
                return;
            GameObject root = breachVisualRoot.gameObject;
            breachVisualRoot = null;
            if (Application.isPlaying)
                Destroy(root);
            else
                DestroyImmediate(root);
        }

        static int StableHash(string value)
        {
            unchecked
            {
                int hash = 17;
                for (int index = 0; index < value.Length; index++)
                    hash = hash * 31 + value[index];
                return hash;
            }
        }
    }

    #endif

    /// <summary>
    /// A combat skybridge is structural cover, not ordinary street dressing.
    /// Normal fire and ship impacts never delete it. A demolition round always
    /// severs it; explosions must be both wide and energetic enough to count as
    /// a major blast. This keeps flight routes readable while preserving the
    /// player's terrain-demolition skill.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UrbanDestructibleBridge : MonoBehaviour, IUrbanDestructible
    {
        const int MaximumActiveRamDebrisHalves = 24;
        const float RamDebrisLifetime = 2.8f;
        static readonly Queue<GameObject> ActiveRamDebris =
            new Queue<GameObject>(MaximumActiveRamDebrisHalves);
        Renderer[] renderers = Array.Empty<Renderer>();
        Collider[] colliders = Array.Empty<Collider>();
        UrbanDestructionCoordinator coordinator;
        Bounds fallbackBounds;
        Material material;
        int stableSeed;
        float minimumLargeExplosionDamage = 92f;
        float minimumLargeExplosionRadius = 18f;
        bool configured;
        bool destroyed;
        Vector3 lastBreakPoint;

        public Bounds DestructionBounds
        {
            get
            {
                Bounds result = fallbackBounds;
                bool initialized = false;
                for (int index = 0; index < renderers.Length; index++)
                {
                    Renderer renderer = renderers[index];
                    if (renderer == null)
                        continue;
                    if (!initialized)
                    {
                        result = renderer.bounds;
                        initialized = true;
                    }
                    else
                    {
                        result.Encapsulate(renderer.bounds);
                    }
                }
                return result;
            }
        }

        public bool IsUrbanDestroyed => destroyed;
        public Vector3 LastBreakPoint => lastBreakPoint;
        public static int ActiveBossRamDebrisHalfCount
        {
            get
            {
                RemoveExpiredRamDebris();
                return ActiveRamDebris.Count;
            }
        }

        public void Configure(
            UrbanDestructionCoordinator targetCoordinator,
            float largeExplosionDamage = 92f,
            float largeExplosionRadius = 18f)
        {
            coordinator = targetCoordinator;
            minimumLargeExplosionDamage = Mathf.Max(10f, largeExplosionDamage);
            minimumLargeExplosionRadius = Mathf.Max(4f, largeExplosionRadius);
            renderers = GetComponentsInChildren<Renderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);
            fallbackBounds = CalculateBounds(renderers, colliders, transform.position);
            material = ResolveMaterial(renderers);
            stableSeed = StableHash(gameObject.name);
            configured = true;
            UrbanDestructionWorld.Register(this);
        }

        void OnEnable()
        {
            if (configured)
                UrbanDestructionWorld.Register(this);
        }

        void OnDisable()
        {
            UrbanDestructionWorld.Unregister(this);
        }

        public bool ApplyUrbanDamage(in UrbanDamageRequest request)
        {
            if (!configured || destroyed || coordinator == null)
                return false;

            bool demolition = request.kind ==
                              UrbanDamageKind.DemolitionProjectile;
            bool energyBlade = request.kind == UrbanDamageKind.EnergyBlade;
            bool largeExplosion = request.kind == UrbanDamageKind.Explosion &&
                                  request.damage >= minimumLargeExplosionDamage &&
                                  request.radius >= minimumLargeExplosionRadius;
            if (!demolition && !energyBlade && !largeExplosion)
                return false;

            Break(
                request.point,
                request.normal,
                request.direction,
                energyBlade ? 1.55f : demolition ? 1.35f : 1f,
                true,
                false);
            return true;
        }

        public bool TryBreakFromBossRam(
            Vector3 contactPoint,
            Vector3 ramDirection)
        {
            if (!configured || destroyed || coordinator == null)
                return false;
            Vector3 direction = ramDirection.sqrMagnitude > 0.001f
                ? ramDirection.normalized
                : transform.forward;
            Break(
                contactPoint,
                -direction,
                direction,
                1.45f,
                true,
                true);
            return true;
        }

        public void ForceBreakFromSupportLoss(Vector3 lostSupportCenter)
        {
            if (!configured || destroyed || coordinator == null)
                return;
            Vector3 direction = DestructionBounds.center - lostSupportCenter;
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector3.down;
            Break(
                DestructionBounds.center,
                Vector3.up,
                direction,
                1.15f,
                false,
                false);
        }

        void Break(
            Vector3 point,
            Vector3 normal,
            Vector3 direction,
            float impulseScale,
            bool showDirectHit,
            bool bossRam)
        {
            destroyed = true;
            Bounds bounds = DestructionBounds;
            lastBreakPoint = bounds.ClosestPoint(point);
            if (showDirectHit)
            {
                coordinator.SpawnImpact(
                    bounds,
                    lastBreakPoint,
                    normal.sqrMagnitude > 0.001f
                        ? normal.normalized
                        : Vector3.up,
                    direction,
                    Mathf.Clamp(bounds.extents.magnitude * 0.18f, 2f, 7f),
                    0.72f,
                    material,
                    stableSeed ^ 486187739,
                    true,
                    null);
            }
            for (int index = 0; index < renderers.Length; index++)
                if (renderers[index] != null)
                    renderers[index].enabled = false;
            for (int index = 0; index < colliders.Length; index++)
                if (colliders[index] != null)
                    colliders[index].enabled = false;
            Vector3 impulse = direction.sqrMagnitude > 0.001f
                ? direction.normalized * impulseScale
                : Vector3.down;
            if (bossRam)
                SpawnBossRamHalves(bounds, lastBreakPoint, impulse);
            else
                coordinator.SpawnDecorationBreak(
                    bounds,
                    impulse,
                    material,
                    stableSeed);
        }

        void SpawnBossRamHalves(
            Bounds bounds,
            Vector3 contactPoint,
            Vector3 impulse)
        {
            Vector3 bridgeForward = transform.forward.normalized;
            Vector3 bridgeRight = transform.right.normalized;
            Vector3 bridgeUp = transform.up.normalized;
            float length = ProjectedSize(bounds.extents, bridgeForward);
            float width = ProjectedSize(bounds.extents, bridgeRight);
            float height = ProjectedSize(bounds.extents, bridgeUp);
            float contactOffset = Vector3.Dot(
                contactPoint - bounds.center,
                bridgeForward);
            float split = Mathf.Clamp(
                contactOffset + length * 0.5f,
                length * 0.25f,
                length * 0.75f);
            float firstLength = split;
            float secondLength = length - split;
            SpawnBossRamHalf(
                "BossRamBridgeHalf_A",
                bounds.center + bridgeForward *
                (-length * 0.5f + firstLength * 0.5f),
                new Vector3(width, height, firstLength),
                impulse - bridgeRight * 2.2f,
                -bridgeForward);
            SpawnBossRamHalf(
                "BossRamBridgeHalf_B",
                bounds.center + bridgeForward *
                (split - length * 0.5f + secondLength * 0.5f),
                new Vector3(width, height, secondLength),
                impulse + bridgeRight * 2.2f,
                bridgeForward);
        }

        void SpawnBossRamHalf(
            string halfName,
            Vector3 position,
            Vector3 size,
            Vector3 velocity,
            Vector3 spinAxis)
        {
            GameObject half = GameObject.CreatePrimitive(PrimitiveType.Cube);
            half.name = halfName;
            half.transform.SetParent(transform.parent, true);
            half.transform.SetPositionAndRotation(position, transform.rotation);
            half.transform.localScale = new Vector3(
                Mathf.Max(0.5f, size.x),
                Mathf.Max(0.25f, size.y),
                Mathf.Max(1f, size.z));
            Collider halfCollider = half.GetComponent<Collider>();
            if (halfCollider != null)
            {
                halfCollider.enabled = false;
                if (Application.isPlaying)
                    Destroy(halfCollider);
                else
                    DestroyImmediate(halfCollider);
            }
            Renderer renderer = half.GetComponent<Renderer>();
            if (renderer != null && material != null)
                renderer.sharedMaterial = material;
            Rigidbody debrisBody = half.AddComponent<Rigidbody>();
            debrisBody.useGravity = true;
            debrisBody.mass = 18f;
            debrisBody.drag = 0.18f;
            debrisBody.angularDrag = 0.12f;
            debrisBody.velocity = velocity * 7f + Vector3.down * 3.5f;
            debrisBody.angularVelocity = spinAxis * 1.8f +
                                         transform.up * 0.7f;
            UrbanBridgeRamDebrisLifetime lifetime =
                half.AddComponent<UrbanBridgeRamDebrisLifetime>();
            lifetime.Configure(RamDebrisLifetime);
            ActiveRamDebris.Enqueue(half);
            TrimRamDebrisBudget();
        }

        static float ProjectedSize(Vector3 extents, Vector3 axis)
        {
            return 2f * (Mathf.Abs(axis.x) * extents.x +
                         Mathf.Abs(axis.y) * extents.y +
                         Mathf.Abs(axis.z) * extents.z);
        }

        static void TrimRamDebrisBudget()
        {
            RemoveExpiredRamDebris();
            while (ActiveRamDebris.Count > MaximumActiveRamDebrisHalves)
            {
                GameObject oldest = ActiveRamDebris.Dequeue();
                if (oldest == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(oldest);
                else
                    DestroyImmediate(oldest);
            }
        }

        static void RemoveExpiredRamDebris()
        {
            while (ActiveRamDebris.Count > 0 &&
                   ActiveRamDebris.Peek() == null)
            {
                ActiveRamDebris.Dequeue();
            }
        }

        static Bounds CalculateBounds(
            Renderer[] sourceRenderers,
            Collider[] sourceColliders,
            Vector3 fallbackCenter)
        {
            Bounds result = new Bounds(fallbackCenter, Vector3.one);
            bool initialized = false;
            for (int index = 0; index < sourceRenderers.Length; index++)
            {
                Renderer renderer = sourceRenderers[index];
                if (renderer == null)
                    continue;
                if (!initialized)
                {
                    result = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    result.Encapsulate(renderer.bounds);
                }
            }
            if (initialized)
                return result;
            for (int index = 0; index < sourceColliders.Length; index++)
            {
                Collider collider = sourceColliders[index];
                if (collider == null)
                    continue;
                if (!initialized)
                {
                    result = collider.bounds;
                    initialized = true;
                }
                else
                {
                    result.Encapsulate(collider.bounds);
                }
            }
            return result;
        }

        static Material ResolveMaterial(Renderer[] source)
        {
            for (int index = 0; index < source.Length; index++)
                if (source[index] != null && source[index].sharedMaterial != null)
                    return source[index].sharedMaterial;
            return null;
        }

        static int StableHash(string value)
        {
            unchecked
            {
                int hash = 17;
                for (int index = 0; index < value.Length; index++)
                    hash = hash * 31 + value[index];
                return hash;
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class UrbanBridgeRamDebrisLifetime : MonoBehaviour
    {
        float destroyAt;

        public void Configure(float lifetime)
        {
            destroyAt = Time.time + Mathf.Max(0.1f, lifetime);
        }

        void Update()
        {
            if (Time.time < destroyAt)
                return;
            Destroy(gameObject);
        }
    }

    [DisallowMultipleComponent]
    public sealed class UrbanDestructibleDecoration : MonoBehaviour, IUrbanDestructible
    {
        Renderer[] renderers = Array.Empty<Renderer>();
        UrbanDestructionCoordinator coordinator;
        Bounds bounds;
        Material material;
        int stableSeed;
        bool configured;
        bool destroyed;

        public Bounds DestructionBounds => bounds;
        public bool IsUrbanDestroyed => destroyed;

        public void Configure(UrbanDestructionCoordinator targetCoordinator)
        {
            coordinator = targetCoordinator;
            renderers = GetComponentsInChildren<Renderer>(true);
            if (!TryCalculateBounds(renderers, out bounds))
                bounds = new Bounds(transform.position, Vector3.one);
            material = renderers.Length > 0 && renderers[0] != null
                ? renderers[0].sharedMaterial
                : null;
            stableSeed = StableHash(gameObject.name);
            configured = true;
            UrbanDestructionWorld.Register(this);
        }

        void OnEnable()
        {
            if (configured)
                UrbanDestructionWorld.Register(this);
        }

        void OnDisable()
        {
            UrbanDestructionWorld.Unregister(this);
        }

        public bool ApplyUrbanDamage(in UrbanDamageRequest request)
        {
            if (!configured || destroyed || coordinator == null)
                return false;
            float threshold = Mathf.Clamp(bounds.extents.magnitude * 0.7f, 4f, 28f);
            float applied = request.kind == UrbanDamageKind.DemolitionProjectile
                ? request.damage * 2f
                : request.damage;
            if (applied < threshold)
                return false;
            destroyed = true;
            for (int index = 0; index < renderers.Length; index++)
                if (renderers[index] != null)
                    renderers[index].enabled = false;
            coordinator.SpawnDecorationBreak(
                bounds,
                request.direction,
                material,
                stableSeed);
            return true;
        }

        static bool TryCalculateBounds(Renderer[] source, out Bounds result)
        {
            result = new Bounds();
            bool initialized = false;
            for (int index = 0; index < source.Length; index++)
            {
                Renderer renderer = source[index];
                if (renderer == null)
                    continue;
                if (!initialized)
                {
                    result = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    result.Encapsulate(renderer.bounds);
                }
            }
            return initialized;
        }

        static int StableHash(string value)
        {
            unchecked
            {
                int hash = 23;
                for (int index = 0; index < value.Length; index++)
                    hash = hash * 31 + value[index];
                return hash;
            }
        }
    }

    public sealed class UrbanDebrisPiece : MonoBehaviour
    {
        UrbanDestructionCoordinator owner;
        MeshFilter meshFilter;
        MeshRenderer meshRenderer;
        BoxCollider boxCollider;
        Rigidbody body;
        Mesh ownedMesh;
        Vector3 debugVelocity;
        float disableCollisionAt;
        float recycleAt;
        bool physical;
        bool collisionDisabled;

        public bool WasPhysical => physical;

        public static UrbanDebrisPiece Create(UrbanDestructionCoordinator owner)
        {
            var gameObject = new GameObject();
            gameObject.name = "PooledUrbanDebris_有限物理碎片";
            gameObject.transform.SetParent(owner.transform, false);
            UrbanDebrisPiece piece = gameObject.AddComponent<UrbanDebrisPiece>();
            piece.owner = owner;
            piece.meshFilter = gameObject.AddComponent<MeshFilter>();
            piece.meshRenderer = gameObject.AddComponent<MeshRenderer>();
            piece.boxCollider = gameObject.AddComponent<BoxCollider>();
            piece.body = gameObject.AddComponent<Rigidbody>();
            piece.ownedMesh = CreateFractureMesh(gameObject.GetInstanceID());
            piece.meshFilter.sharedMesh = piece.ownedMesh;
            gameObject.SetActive(false);
            return piece;
        }

        internal static Mesh CreateFractureMesh(int seed)
        {
            var random = new System.Random(seed);
            Vector3[] corners =
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f,  0.5f, -0.5f),
                new Vector3( 0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f, -0.5f,  0.5f),
                new Vector3(-0.5f,  0.5f,  0.5f),
                new Vector3( 0.5f,  0.5f,  0.5f)
            };
            for (int index = 0; index < corners.Length; index++)
            {
                float inset = Mathf.Lerp(0.02f, 0.17f, (float)random.NextDouble());
                corners[index] += new Vector3(
                    ((float)random.NextDouble() * 2f - 1f) * inset,
                    ((float)random.NextDouble() * 2f - 1f) * inset,
                    ((float)random.NextDouble() * 2f - 1f) * inset);
            }
            int[,] faces =
            {
                { 0, 2, 3, 1 },
                { 4, 5, 7, 6 },
                { 0, 4, 6, 2 },
                { 1, 3, 7, 5 },
                { 0, 1, 5, 4 },
                { 2, 6, 7, 3 }
            };
            var vertices = new Vector3[24];
            var triangles = new int[36];
            var uv = new Vector2[24];
            for (int face = 0; face < 6; face++)
            {
                int vertex = face * 4;
                vertices[vertex] = corners[faces[face, 0]];
                vertices[vertex + 1] = corners[faces[face, 1]];
                vertices[vertex + 2] = corners[faces[face, 2]];
                vertices[vertex + 3] = corners[faces[face, 3]];
                uv[vertex] = new Vector2(0f, 0f);
                uv[vertex + 1] = new Vector2(0f, 1f);
                uv[vertex + 2] = new Vector2(1f, 1f);
                uv[vertex + 3] = new Vector2(1f, 0f);
                int triangle = face * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 1;
                triangles[triangle + 2] = vertex + 2;
                triangles[triangle + 3] = vertex;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }
            var mesh = new Mesh
            {
                name = "UrbanFractureCell_24Verts",
                vertices = vertices,
                triangles = triangles,
                uv = uv
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Launch(
            Vector3 position,
            Vector3 size,
            Quaternion rotation,
            Vector3 velocity,
            Material material,
            bool usePhysics,
            float collisionSeconds,
            float lifetime)
        {
            physical = usePhysics;
            collisionDisabled = !usePhysics;
            transform.position = position;
            transform.rotation = rotation;
            transform.localScale = new Vector3(
                Mathf.Max(0.15f, size.x),
                Mathf.Max(0.15f, size.y),
                Mathf.Max(0.15f, size.z));
            meshRenderer.sharedMaterial = material;
            boxCollider.enabled = usePhysics;
            body.isKinematic = !usePhysics;
            body.detectCollisions = usePhysics;
            body.useGravity = usePhysics;
            body.mass = Mathf.Clamp(size.magnitude * 0.42f, 1.5f, 18f);
            body.drag = 0.08f;
            body.angularDrag = 0.18f;
            body.maxAngularVelocity = 12f;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            debugVelocity = velocity;
            gameObject.SetActive(true);
            if (usePhysics)
            {
                body.velocity = velocity;
                body.angularVelocity = new Vector3(3.1f, 4.7f, 2.3f);
                body.WakeUp();
            }
            disableCollisionAt = Time.unscaledTime + collisionSeconds;
            recycleAt = Time.unscaledTime + lifetime;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (!collisionDisabled && now >= disableCollisionAt)
            {
                collisionDisabled = true;
                boxCollider.enabled = false;
                body.detectCollisions = false;
                body.isKinematic = true;
            }
            if (now >= recycleAt)
                owner.ReleaseDebris(this);
        }

        public void DebugAdvance(float seconds)
        {
            if (Application.isPlaying || !gameObject.activeSelf)
                return;
            transform.position += debugVelocity * seconds +
                                  Physics.gravity * (0.5f * seconds * seconds);
            transform.Rotate(new Vector3(53f, 71f, 37f) * seconds, Space.Self);
        }

        public void PrepareForPool()
        {
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            body.detectCollisions = false;
            boxCollider.enabled = false;
            gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (ownedMesh == null)
                return;
            if (Application.isPlaying)
                Destroy(ownedMesh);
            else
                DestroyImmediate(ownedMesh);
        }
    }

    public sealed class UrbanDustBurst : MonoBehaviour
    {
        UrbanDestructionCoordinator owner;
        ParticleSystem particles;
        float releaseAt;

        public static UrbanDustBurst Create(
            UrbanDestructionCoordinator owner,
            Material material)
        {
            var gameObject = new GameObject("PooledUrbanDust_粉尘烟云");
            gameObject.transform.SetParent(owner.transform, false);
            ParticleSystem system = gameObject.AddComponent<ParticleSystem>();
            ParticleSystemRenderer renderer =
                gameObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 180;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            UrbanDustBurst burst = gameObject.AddComponent<UrbanDustBurst>();
            burst.owner = owner;
            burst.particles = system;
            gameObject.SetActive(false);
            return burst;
        }

        public void Play(
            Vector3 position,
            Vector3 normal,
            float radius,
            bool heavy)
        {
            transform.position = position;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
            gameObject.SetActive(true);
            particles.Clear(true);
            var main = particles.main;
            main.startLifetime = heavy
                ? new ParticleSystem.MinMaxCurve(1.8f, 3.4f)
                : new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
            main.startSpeed = heavy
                ? new ParticleSystem.MinMaxCurve(radius * 0.7f, radius * 1.8f)
                : new ParticleSystem.MinMaxCurve(radius * 0.4f, radius * 1.1f);
            main.startSize = heavy
                ? new ParticleSystem.MinMaxCurve(radius * 0.12f, radius * 0.38f)
                : new ParticleSystem.MinMaxCurve(radius * 0.08f, radius * 0.24f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.28f, 0.25f, 0.22f, 0.46f),
                new Color(0.56f, 0.50f, 0.42f, 0.22f));
            main.gravityModifier = heavy ? 0.16f : 0.08f;
            var shape = particles.shape;
            shape.radius = Mathf.Clamp(radius * 0.3f, 0.5f, 12f);
            int count = heavy
                ? Mathf.Clamp(Mathf.RoundToInt(radius * 7f), 64, 170)
                : Mathf.Clamp(Mathf.RoundToInt(radius * 4f), 18, 80);
            particles.Emit(count);
            releaseAt = Time.unscaledTime + (heavy ? 3.6f : 2.1f);
        }

        void Update()
        {
            if (Time.unscaledTime >= releaseAt)
                owner.ReleaseDust(this);
        }

        public void DebugAdvance(float seconds)
        {
            if (!gameObject.activeSelf)
                return;
            particles.Simulate(seconds, true, false, true);
        }

        public void PrepareForPool()
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            gameObject.SetActive(false);
        }
    }

    [DisallowMultipleComponent]
    public sealed class UrbanDestructibleRuinSection : MonoBehaviour, IUrbanDestructible
    {
        UrbanDestructionCoordinator coordinator;
        Collider persistentCollider;
        Material facadeMaterial;
        Transform breachVisualRoot;
        float maximumIntegrity;
        float integrity;
        int stableSeed;
        int hitCount;
        int fractureStage;
        int fractureGeneration;
        bool configured;
        bool retired;

        public Bounds DestructionBounds
        {
            get
            {
                if (persistentCollider != null)
                    return persistentCollider.bounds;
                Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                    return new Bounds(transform.position, Vector3.one);
                Bounds bounds = renderers[0].bounds;
                for (int index = 1; index < renderers.Length; index++)
                    if (renderers[index] != null)
                        bounds.Encapsulate(renderers[index].bounds);
                return bounds;
            }
        }

        public bool IsUrbanDestroyed => retired;
        public int FractureStage => fractureStage;
        public int FractureGeneration => fractureGeneration;
        public Collider PersistentCollider => persistentCollider;
        internal Transform BreachVisualRoot => EnsureBreachVisualRoot();

        public void Configure(
            UrbanDestructionCoordinator targetCoordinator,
            Material targetFacadeMaterial,
            int targetStableSeed,
            string sectionLabel,
            Collider targetCollider,
            int targetFractureGeneration = 0)
        {
            coordinator = targetCoordinator;
            facadeMaterial = targetFacadeMaterial;
            stableSeed = targetStableSeed;
            persistentCollider = targetCollider != null
                ? targetCollider
                : GetComponentInChildren<Collider>(true);
            fractureGeneration = Mathf.Max(0, targetFractureGeneration);
            retired = false;
            Bounds bounds = DestructionBounds;
            int generationForTuning = Mathf.Min(fractureGeneration, 12);
            maximumIntegrity = Mathf.Clamp(
                (52f + bounds.extents.magnitude * 3.2f) /
                (1f + generationForTuning * 0.42f),
                fractureGeneration == 0 ? 72f : 34f,
                fractureGeneration == 0 ? 260f : 150f);
            integrity = maximumIntegrity;
            configured = true;
            name = sectionLabel + "_可持续切割_保留碰撞";
            if (persistentCollider != null)
                persistentCollider.enabled = true;
            UrbanDestructionWorld.Register(this);
        }

        void OnEnable()
        {
            if (configured && !retired)
                UrbanDestructionWorld.Register(this);
        }

        void OnDisable()
        {
            UrbanDestructionWorld.Unregister(this);
        }

        public bool ApplyUrbanDamage(in UrbanDamageRequest request)
        {
            if (!configured || retired || coordinator == null ||
                request.damage <= 0f)
            {
                return false;
            }

            if (request.kind == UrbanDamageKind.EnergyBlade)
            {
                hitCount++;
                bool sliced = coordinator.TrySliceRuinSection(
                    this,
                    request,
                    facadeMaterial,
                    stableSeed ^ hitCount * 49979687);
                if (sliced)
                {
                    if (fractureStage < int.MaxValue)
                        fractureStage++;
                    return true;
                }
                // Very small pieces and the global collider budget stop the
                // binary tree. They still show a hit, but never spawn a fake
                // cube in place of a geometric cut.
                coordinator.SpawnRuinSectionDamage(
                    this,
                    request,
                    facadeMaterial,
                    stableSeed ^ hitCount * 49979687,
                    0);
                return true;
            }

            float applied;
            if (request.kind == UrbanDamageKind.DemolitionProjectile)
            {
                applied = coordinator.ResolveDemolitionDamage(
                    maximumIntegrity,
                    request.damage);
            }
            else
            {
                applied = coordinator.ScaleDamage(
                    request.kind,
                    request.damage);
            }
            if (applied <= 0.001f)
                return false;

            integrity = Mathf.Max(0f, integrity - applied);
            hitCount++;
            // Two full demolition hits leave roughly four percent integrity;
            // use a small tolerance so float rounding cannot suppress the
            // intended first secondary-fracture stage.
            bool stageBroken = integrity <= maximumIntegrity * 0.05f;
            if (stageBroken)
            {
                if (fractureStage < int.MaxValue)
                    fractureStage++;
                integrity = maximumIntegrity;
            }

            coordinator.SpawnRuinSectionDamage(
                this,
                request,
                facadeMaterial,
                stableSeed ^ hitCount * 49979687,
                stageBroken ? 1 : 0);

            // The source section remains a physical, damageable ruin after
            // every cut.  Child generation is bounded by actual piece size and
            // a global collider budget rather than by a one-shot generation cap.
            if (persistentCollider != null)
                persistentCollider.enabled = true;
            return true;
        }

        internal Renderer[] GetOwnedSliceRenderers()
        {
            UrbanSliceVisual[] markers =
                GetComponentsInChildren<UrbanSliceVisual>(true);
            var renderers = new List<Renderer>(markers.Length);
            for (int index = 0; index < markers.Length; index++)
            {
                UrbanSliceVisual marker = markers[index];
                if (marker == null)
                    continue;
                UrbanDestructibleRuinSection owner =
                    marker.GetComponentInParent<UrbanDestructibleRuinSection>();
                if (owner != this)
                    continue;
                Renderer renderer = marker.GetComponent<Renderer>();
                if (renderer != null && renderer.enabled)
                    renderers.Add(renderer);
            }
            return renderers.ToArray();
        }

        internal void RetireAfterSlice(Renderer[] sourceRenderers)
        {
            if (retired)
                return;
            if (sourceRenderers != null)
            {
                for (int index = 0; index < sourceRenderers.Length; index++)
                    if (sourceRenderers[index] != null)
                        sourceRenderers[index].enabled = false;
            }
            if (persistentCollider != null)
                persistentCollider.enabled = false;
            retired = true;
            UrbanDestructionWorld.Unregister(this);
            enabled = false;
        }

        Transform EnsureBreachVisualRoot()
        {
            if (breachVisualRoot != null)
                return breachVisualRoot;
            breachVisualRoot = new GameObject(
                "SecondaryDamage_命中位置保留").transform;
            breachVisualRoot.SetParent(transform, false);
            return breachVisualRoot;
        }
    }

    /// <summary>
    /// A deterministic, bounded-cost rigid-section topple.  The pivot is placed
    /// on the fractured facade edge, so the original upper building shell turns
    /// around the hit-dependent cut instead of being replaced by random cubes.
    /// </summary>
    public sealed class UrbanTopplingSection : MonoBehaviour
    {
        BoxCollider sectionCollider;
        Rigidbody body;
        Quaternion initialRotation;
        Vector3 worldAxis;
        Vector3 fallDirection;
        float cutHeight;
        float maximumAngle;
        float duration;
        float elapsed;
        float currentAngle;
        float lastSweepSpeed;
        float lastSweepValidUntil = -100f;
        float lastVehicleImpactAt = -100f;

        public float CutHeight => cutHeight;
        public Vector3 FallDirection => fallDirection;
        public float CurrentAngle => currentAngle;

        public void Configure(
            BoxCollider targetCollider,
            Rigidbody targetBody,
            Vector3 rotationAxis,
            Vector3 targetFallDirection,
            float targetCutHeight,
            float targetMaximumAngle,
            float targetDuration)
        {
            sectionCollider = targetCollider;
            body = targetBody;
            initialRotation = transform.rotation;
            worldAxis = rotationAxis.sqrMagnitude > 0.001f
                ? rotationAxis.normalized
                : Vector3.right;
            fallDirection = targetFallDirection.sqrMagnitude > 0.001f
                ? targetFallDirection.normalized
                : Vector3.forward;
            cutHeight = targetCutHeight;
            maximumAngle = Mathf.Clamp(targetMaximumAngle, 48f, 86f);
            duration = Mathf.Max(0.45f, targetDuration);
            elapsed = 0f;
            currentAngle = 0f;
        }

        void Update()
        {
            Advance(Time.unscaledDeltaTime, true);
        }

        public void DebugAdvance(float seconds)
        {
            if (Application.isPlaying)
                return;
            Advance(seconds, false);
        }

        void Advance(float seconds, bool useRigidbody)
        {
            if (seconds <= 0f || currentAngle >= maximumAngle - 0.01f)
                return;
            float previousAngle = currentAngle;
            elapsed = Mathf.Min(duration, elapsed + seconds);
            float t = Mathf.Clamp01(elapsed / duration);
            // A supported tower hesitates briefly, then accelerates under its
            // own mass.  Smooth the final contact to avoid a one-frame snap.
            float gravityEase = t * t;
            gravityEase = gravityEase * (3f - 2f * gravityEase);
            currentAngle = maximumAngle * gravityEase;
            float angularSpeed = Mathf.Abs(currentAngle - previousAngle) *
                                 Mathf.Deg2Rad / Mathf.Max(0.0001f, seconds);
            float sweepRadius = sectionCollider != null
                ? sectionCollider.bounds.extents.magnitude
                : Mathf.Max(1f, cutHeight * 0.5f);
            lastSweepSpeed = angularSpeed * sweepRadius;
            if (useRigidbody)
            {
                lastSweepValidUntil = Time.unscaledTime +
                                      Mathf.Max(0.08f, Time.fixedDeltaTime * 2f);
            }
            Quaternion target = Quaternion.AngleAxis(
                currentAngle,
                worldAxis) * initialRotation;
            if (useRigidbody && body != null)
                body.MoveRotation(target);
            else
                transform.rotation = target;

            // The fallen tower is persistent level geometry.  Its inexpensive
            // box proxy remains active both during and after the animation; only
            // short-lived debris particles relinquish collision for performance.
            if (sectionCollider != null)
                sectionCollider.enabled = true;
            if (body != null)
                body.detectCollisions = true;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision == null || collision.contactCount == 0 ||
                Time.unscaledTime - lastVehicleImpactAt < 0.45f)
            {
                return;
            }
            ContactPoint contact = collision.GetContact(0);
            float speed = Mathf.Max(
                collision.relativeVelocity.magnitude,
                Time.unscaledTime <= lastSweepValidUntil
                    ? lastSweepSpeed
                    : 0f);
            float impulse = Mathf.Max(
                collision.impulse.magnitude,
                body != null ? body.mass * speed : speed * 120f);
            if (UrbanVehicleImpactPolicy.TryApplyModuleDamage(
                    contact.thisCollider,
                    contact.otherCollider,
                    transform,
                    contact.point,
                    fallDirection + Vector3.down * 0.35f,
                    speed,
                    impulse,
                    gameObject))
            {
                lastVehicleImpactAt = Time.unscaledTime;
            }
        }
    }

    public sealed class UrbanRuntimeResourceOwner : MonoBehaviour
    {
        readonly List<Material> ownedMaterials = new List<Material>(12);
        readonly List<Mesh> ownedMeshes = new List<Mesh>(2);

        public void Configure(
            IEnumerable<Material> materials,
            IEnumerable<Mesh> meshes)
        {
            if (materials != null)
                ownedMaterials.AddRange(materials);
            if (meshes != null)
                ownedMeshes.AddRange(meshes);
        }

        void OnDestroy()
        {
            for (int index = 0; index < ownedMaterials.Count; index++)
                DestroyOwned(ownedMaterials[index]);
            for (int index = 0; index < ownedMeshes.Count; index++)
                DestroyOwned(ownedMeshes[index]);
        }

        static void DestroyOwned(UnityEngine.Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }

    public sealed class UrbanRuntimeMeshOwner : MonoBehaviour
    {
        Mesh ownedMesh;

        public void Configure(Mesh mesh)
        {
            ownedMesh = mesh;
        }

        void OnDestroy()
        {
            if (ownedMesh == null)
                return;
            if (Application.isPlaying)
                Destroy(ownedMesh);
            else
                DestroyImmediate(ownedMesh);
        }
    }

    public sealed class UrbanTransientVisual : MonoBehaviour
    {
        float lifetime;
        float startedAt;
        float targetScale;
        bool expand;

        public void Configure(
            float seconds,
            bool shouldExpand,
            UrbanDestructionCoordinator owner,
            float maximumScale = 1f)
        {
            lifetime = Mathf.Max(0.05f, seconds);
            startedAt = Time.unscaledTime;
            expand = shouldExpand;
            targetScale = Mathf.Max(1f, maximumScale);
            if (expand)
                transform.localScale = Vector3.one * 0.05f;
        }

        void Update()
        {
            float elapsed = Time.unscaledTime - startedAt;
            ApplyExpansion(elapsed);
            if (elapsed < lifetime)
                return;
            if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
        }

        public void DebugAdvance(float seconds)
        {
            if (!expand || seconds <= 0f)
                return;
            ApplyExpansion(seconds);
        }

        void ApplyExpansion(float elapsed)
        {
            if (!expand)
                return;
            float t = Mathf.Clamp01(elapsed / lifetime);
            transform.localScale = Vector3.one *
                                   Mathf.Lerp(
                                       0.05f,
                                       targetScale,
                                       1f - (1f - t) * (1f - t));
            LineRenderer line = GetComponent<LineRenderer>();
            if (line == null)
                return;
            Color color = line.startColor;
            color.a = 1f - t;
            line.startColor = color;
            line.endColor = color;
        }
    }
}
