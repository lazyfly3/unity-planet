using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.CityPcg
{
    /// <summary>
    /// Runtime-generated structural proxy for any PCG/imported building.
    /// Art meshes stay independent from the damage graph: cells and cheap box
    /// colliders are derived from the authored bounds, so replacement art uses
    /// the same destruction rules without hand-authored fracture prefabs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UrbanDestructibleBuilding : MonoBehaviour, IUrbanDestructible
    {
        enum StructuralRole
        {
            Facade,
            Column,
            Core
        }

        sealed class StructuralCell
        {
            public int x;
            public int z;
            public int level;
            public StructuralRole role;
            public Bounds bounds;
            public float maximumIntegrity;
            public float integrity;
            public bool active = true;
            public bool supported;
            public bool pendingDetach;
        }

        sealed class PendingStructuralGroup
        {
            public readonly List<StructuralCell> cells =
                new List<StructuralCell>(16);
            public float detachAt;
            public Vector3 direction;
            public Material facade;
            public int stableSeed;
        }

        readonly List<StructuralCell> structuralCells =
            new List<StructuralCell>(96);
        readonly List<Bounds> damageHoles = new List<Bounds>(8);
        readonly List<Renderer> damageRenderers = new List<Renderer>(16);
        readonly List<Material> damageMaterials = new List<Material>(16);
        readonly List<Mesh> damageMeshes = new List<Mesh>(4);
        readonly List<PendingStructuralGroup> pendingGroups =
            new List<PendingStructuralGroup>(4);

        Renderer[] originalRenderers = Array.Empty<Renderer>();
        Collider[] originalColliders = Array.Empty<Collider>();
        UrbanDestructionCoordinator coordinator;
        Vector3 designSize;
        string stableId = string.Empty;
        float maximumIntegrity;
        float integrity;
        float hiddenTopplingFatigue;
        float accumulatedChipDamage;
        float lastCollapseCutHeight;
        Vector3 lastCollapseImpactPoint;
        Vector3 lastCollapseDirection;
        int breachCount;
        int stableHash;
        int gridColumns;
        int gridLevels;
        int brokenStructuralCells;
        int detachedClusterCount;
        bool configured;
        bool collapsed;
        bool lastCollapseWasEnergyBlade;
        Material facadeMaterial;
        Transform breachVisualRoot;
        Transform damageVisualRoot;
        Transform damageInteriorRoot;
        Transform collisionProxyRoot;

        public Bounds DestructionBounds
        {
            get
            {
                Bounds result = new Bounds(
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
                        result = renderer.bounds;
                        initialized = true;
                    }
                    else
                    {
                        result.Encapsulate(renderer.bounds);
                    }
                }
                if (!initialized)
                    result.size = designSize;
                return result;
            }
        }

        public bool IsUrbanDestroyed => collapsed;
        public bool IsCollapsed => collapsed;
        public float CurrentIntegrity => Mathf.Max(
            0f,
            integrity - hiddenTopplingFatigue);
        public float MaximumIntegrity => maximumIntegrity;
        public float Integrity01 => maximumIntegrity <= 0.01f
            ? 0f
            : Mathf.Clamp01(CurrentIntegrity / maximumIntegrity);
        public Vector3 DesignSize => designSize;
        public string StableId => stableId;
        public int BreachCount => breachCount;
        public int TotalStructuralCells => structuralCells.Count;
        public int ActiveStructuralCells => CountActiveCells();
        public int BrokenStructuralCells => brokenStructuralCells;
        public int DetachedClusterCount => detachedClusterCount;
        public int PendingStructuralClusterCount => pendingGroups.Count;
        public int ActiveCollisionProxyCount => collisionProxyRoot != null
            ? collisionProxyRoot.GetComponentsInChildren<BoxCollider>(true).Length
            : 0;
        public bool HasLocalizedDamageVisual => damageVisualRoot != null;
        public bool HasDamageCavityInterior => damageInteriorRoot != null;
        public int DamageCavityPieceCount => damageInteriorRoot != null
            ? damageInteriorRoot.GetComponentsInChildren<Renderer>(true).Length
            : 0;
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
            hiddenTopplingFatigue = 0f;
            originalRenderers = GetComponentsInChildren<Renderer>(true);
            originalColliders = GetComponentsInChildren<Collider>(true);
            facadeMaterial = ResolveFacadeMaterial(originalRenderers);
            BuildStructuralGraph(DestructionBounds);
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

        void OnDestroy()
        {
            DestroyRuntimeRoot(ref damageVisualRoot);
            DestroyRuntimeRoot(ref collisionProxyRoot);
        }

        void Update()
        {
            if (configured && !collapsed && pendingGroups.Count > 0)
                ProcessPendingStructuralGroups(Time.unscaledTime);
        }

        void BuildStructuralGraph(Bounds bounds)
        {
            structuralCells.Clear();
            damageHoles.Clear();
            brokenStructuralCells = 0;
            detachedClusterCount = 0;
            pendingGroups.Clear();
            gridColumns = coordinator != null ? coordinator.StructuralColumns : 5;
            gridLevels = Mathf.Clamp(
                Mathf.RoundToInt(bounds.size.y /
                                 (coordinator != null
                                     ? coordinator.TargetStructuralFloorHeight
                                     : 9f)),
                4,
                20);
            Vector3 cellSize = new Vector3(
                bounds.size.x / gridColumns,
                bounds.size.y / gridLevels,
                bounds.size.z / gridColumns);
            int upperMiddle = gridColumns / 2;
            int lowerMiddle = (gridColumns - 1) / 2;
            for (int level = 0; level < gridLevels; level++)
            for (int z = 0; z < gridColumns; z++)
            for (int x = 0; x < gridColumns; x++)
            {
                bool inCore = (x == lowerMiddle || x == upperMiddle) &&
                              (z == lowerMiddle || z == upperMiddle);
                StructuralRole role = inCore
                    ? StructuralRole.Core
                    : (x == 0 || x == gridColumns - 1) &&
                      (z == 0 || z == gridColumns - 1)
                        ? StructuralRole.Column
                        : StructuralRole.Facade;
                float roleIntegrity = role == StructuralRole.Core
                    ? 182f
                    : role == StructuralRole.Column
                        ? 122f
                        : 76f;
                if (level == 0)
                    roleIntegrity *= 1.22f;
                Vector3 center = bounds.min + new Vector3(
                    (x + 0.5f) * cellSize.x,
                    (level + 0.5f) * cellSize.y,
                    (z + 0.5f) * cellSize.z);
                structuralCells.Add(new StructuralCell
                {
                    x = x,
                    z = z,
                    level = level,
                    role = role,
                    bounds = new Bounds(center, cellSize),
                    maximumIntegrity = roleIntegrity,
                    integrity = roleIntegrity,
                    active = true,
                    supported = level == 0,
                    pendingDetach = false
                });
            }
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
                Collapse(request);
                return true;
            }

            List<StructuralCell> broken = DamageStructuralCells(request, applied);
            bool strongVisual = request.kind == UrbanDamageKind.DemolitionProjectile ||
                                request.kind == UrbanDamageKind.HighSpeedImpact ||
                                (request.kind == UrbanDamageKind.Explosion && applied >= 18f);
            accumulatedChipDamage += applied;
            if (strongVisual || broken.Count > 0 ||
                accumulatedChipDamage >= maximumIntegrity * 0.09f)
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
                    request.kind == UrbanDamageKind.DemolitionProjectile ||
                    request.kind == UrbanDamageKind.HighSpeedImpact,
                    EnsureBreachVisualRoot());
            }

            if (broken.Count > 0)
            {
                for (int index = 0; index < broken.Count; index++)
                    AddDamageHole(broken[index].bounds);
                List<List<StructuralCell>> unsupported =
                    FindUnsupportedStructuralGroups();
                for (int groupIndex = 0;
                     groupIndex < unsupported.Count;
                     groupIndex++)
                {
                    List<StructuralCell> group = unsupported[groupIndex];
                    ScheduleOrDetachStructuralGroup(
                        group,
                        request.direction,
                        ResolveFacadeMaterialAt(request.point),
                        stableHash ^ breachCount * 19349663 ^
                        groupIndex * 486187739);
                }
                EnsureLocalizedDamageRepresentation();
                UpdateDamageVisuals();
                RebuildDamageCavityInterior();
                RebuildCollisionProxies();
            }

            RefreshIntegrity();
            if (CountActiveCells() == 0)
                collapsed = true;
            return true;
        }

        internal bool ApplyTopplingImpact(
            Vector3 point,
            Vector3 direction,
            float impactSpeed,
            float fallingMass,
            float contactRadius,
            float damage,
            GameObject source)
        {
            if (!configured || collapsed || coordinator == null)
                return false;

            Vector3 safeDirection = direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : Vector3.down;
            float safeSpeed = Mathf.Max(0f, impactSpeed);
            float safeMass = Mathf.Max(1f, fallingMass);
            float impulse = safeMass * Mathf.Max(safeSpeed, 1f);
            var request = new UrbanDamageRequest(
                point,
                -safeDirection,
                safeDirection,
                Mathf.Max(0f, damage),
                Mathf.Clamp(contactRadius, 3f, 14f),
                impulse,
                UrbanDamageKind.HighSpeedImpact,
                source);

            // Tower-to-tower contact deliberately bypasses the ordinary facade
            // breach pipeline.  It only accumulates hidden structural fatigue;
            // no hole shader, cavity, debris or impact VFX is spawned here.
            float hiddenDamage = Mathf.Clamp(
                damage * 0.12f,
                4f,
                maximumIntegrity * 0.18f);
            hiddenTopplingFatigue = Mathf.Min(
                maximumIntegrity,
                hiddenTopplingFatigue + hiddenDamage);

            Bounds bounds = DestructionBounds;
            float targetMass = Mathf.Clamp(
                bounds.size.x * bounds.size.y * bounds.size.z * 0.0025f,
                120f,
                2200f);
            float impactEnergy = 0.5f * safeMass * safeSpeed * safeSpeed;
            float height01 = Mathf.InverseLerp(bounds.min.y, bounds.max.y, point.y);
            float leverageFactor = Mathf.Lerp(1.18f, 0.72f, height01);
            float integrityFactor = Mathf.Lerp(0.48f, 1f, Integrity01);
            float collapseThreshold = 0.5f * targetMass * 38f * 38f *
                                      leverageFactor * integrityFactor;
            if (safeSpeed < 18f ||
                (impactEnergy < collapseThreshold && CurrentIntegrity > 0.01f))
                return true;

            CollapseFromTopplingImpact(request, bounds);
            return true;
        }

        void CollapseFromTopplingImpact(
            in UrbanDamageRequest request,
            Bounds collapseBounds)
        {
            if (collapsed || coordinator == null)
                return;

            lastCollapseImpactPoint = request.point;
            lastCollapseCutHeight = Mathf.Lerp(
                collapseBounds.min.y,
                collapseBounds.max.y,
                0.10f);
            lastCollapseWasEnergyBlade = false;
            lastCollapseDirection = Vector3.ProjectOnPlane(
                request.direction,
                Vector3.up).normalized;
            if (lastCollapseDirection.sqrMagnitude < 0.001f)
                lastCollapseDirection = transform.forward;

            Material collapseFacade = ResolveFacadeMaterialAt(request.point);
            bool replacementReady = coordinator.SpawnCollapse(
                this,
                request.point,
                lastCollapseDirection,
                collapseFacade,
                stableHash ^ breachCount * 19349663 ^ 83492791);
            if (!replacementReady)
                return;

            FinalizeCollapse(collapseBounds);
        }

        List<StructuralCell> DamageStructuralCells(
            in UrbanDamageRequest request,
            float applied)
        {
            var broken = new List<StructuralCell>(4);
            StructuralCell primary = null;
            float nearest = float.PositiveInfinity;
            for (int index = 0; index < structuralCells.Count; index++)
            {
                StructuralCell cell = structuralCells[index];
                if (!cell.active || cell.pendingDetach)
                    continue;
                float distance = cell.bounds.SqrDistance(request.point);
                if (distance < nearest)
                {
                    nearest = distance;
                    primary = cell;
                }
            }
            if (primary == null)
                return broken;

            float primaryScale = request.kind == UrbanDamageKind.Explosion
                ? 0.82f
                : request.kind == UrbanDamageKind.HighSpeedImpact
                    ? 0.72f
                    : request.kind == UrbanDamageKind.DemolitionProjectile
                        ? 0.86f
                        : 0.58f;
            float radius = Mathf.Max(0f, request.radius);
            for (int index = 0; index < structuralCells.Count; index++)
            {
                StructuralCell cell = structuralCells[index];
                if (!cell.active || cell.pendingDetach)
                    continue;
                float damageScale = cell == primary ? primaryScale : 0f;
                if (cell != primary && radius > 0.01f)
                {
                    float distance = Mathf.Sqrt(cell.bounds.SqrDistance(request.point));
                    if (distance <= radius)
                    {
                        float splash = 1f - Mathf.Clamp01(distance / radius);
                        float splashScale = request.kind == UrbanDamageKind.Explosion
                            ? 0.42f
                            : request.kind == UrbanDamageKind.HighSpeedImpact
                                ? 0.22f
                                : request.kind == UrbanDamageKind.DemolitionProjectile
                                    ? 0.14f
                                    : 0.04f;
                        damageScale = splash * splashScale;
                    }
                }
                if (damageScale <= 0.001f)
                    continue;
                float roleResistance = cell.role == StructuralRole.Core
                    ? 0.72f
                    : cell.role == StructuralRole.Column
                        ? 0.86f
                        : 1f;
                cell.integrity = Mathf.Max(
                    0f,
                    cell.integrity - applied * damageScale * roleResistance);
                if (cell.integrity > 0.01f)
                    continue;
                cell.active = false;
                cell.supported = false;
                brokenStructuralCells++;
                broken.Add(cell);
            }
            return broken;
        }

        List<List<StructuralCell>> FindUnsupportedStructuralGroups()
        {
            EvaluateStructuralSupport();
            var unsupported = new HashSet<StructuralCell>();
            for (int index = 0; index < structuralCells.Count; index++)
            {
                StructuralCell cell = structuralCells[index];
                if (cell.active && !cell.supported && !cell.pendingDetach)
                    unsupported.Add(cell);
            }

            var groups = new List<List<StructuralCell>>();
            while (unsupported.Count > 0)
            {
                StructuralCell start = null;
                foreach (StructuralCell candidate in unsupported)
                {
                    start = candidate;
                    break;
                }
                if (start == null)
                    break;
                var group = new List<StructuralCell>();
                var queue = new Queue<StructuralCell>();
                queue.Enqueue(start);
                unsupported.Remove(start);
                while (queue.Count > 0)
                {
                    StructuralCell cell = queue.Dequeue();
                    group.Add(cell);
                    for (int index = structuralCells.Count - 1; index >= 0; index--)
                    {
                        StructuralCell neighbour = structuralCells[index];
                        if (!unsupported.Contains(neighbour) ||
                            ManhattanDistance(cell, neighbour) != 1)
                        {
                            continue;
                        }
                        unsupported.Remove(neighbour);
                        queue.Enqueue(neighbour);
                    }
                }
                groups.Add(group);
            }
            return groups;
        }

        void ScheduleOrDetachStructuralGroup(
            List<StructuralCell> group,
            Vector3 direction,
            Material material,
            int seed)
        {
            if (group == null || group.Count == 0)
                return;
            if (!Application.isPlaying)
            {
                DetachStructuralGroupNow(group, direction, material, seed);
                return;
            }

            var pending = new PendingStructuralGroup
            {
                detachAt = Time.unscaledTime + Mathf.Clamp(
                    0.22f + group.Count * 0.0045f,
                    0.26f,
                    0.72f),
                direction = direction,
                facade = material,
                stableSeed = seed
            };
            for (int index = 0; index < group.Count; index++)
            {
                StructuralCell cell = group[index];
                if (cell == null || !cell.active)
                    continue;
                cell.pendingDetach = true;
                cell.supported = false;
                pending.cells.Add(cell);
            }
            if (pending.cells.Count == 0)
                return;
            pendingGroups.Add(pending);
            var bounds = new List<Bounds>(pending.cells.Count);
            for (int index = 0; index < pending.cells.Count; index++)
                bounds.Add(pending.cells[index].bounds);
            coordinator.SpawnStructuralFailureWarning(
                CombinedBounds(bounds),
                direction);
        }

        void ProcessPendingStructuralGroups(float currentTime)
        {
            for (int index = pendingGroups.Count - 1; index >= 0; index--)
            {
                PendingStructuralGroup pending = pendingGroups[index];
                if (pending == null || currentTime < pending.detachAt)
                    continue;
                pendingGroups.RemoveAt(index);
                DetachStructuralGroupNow(
                    pending.cells,
                    pending.direction,
                    pending.facade,
                    pending.stableSeed);
            }
        }

        void DetachStructuralGroupNow(
            List<StructuralCell> group,
            Vector3 direction,
            Material material,
            int seed)
        {
            if (group == null || group.Count == 0 || coordinator == null)
                return;
            var groupBounds = new List<Bounds>(group.Count);
            for (int index = 0; index < group.Count; index++)
            {
                StructuralCell cell = group[index];
                if (cell == null || !cell.active)
                    continue;
                cell.pendingDetach = false;
                cell.active = false;
                cell.supported = false;
                groupBounds.Add(cell.bounds);
            }
            if (groupBounds.Count == 0)
                return;

            List<Bounds> visualRegions = coordinator.BuildStructuralRegions(
                groupBounds,
                coordinator.MaximumVisibleStructuralHoles);
            for (int index = 0; index < visualRegions.Count; index++)
                AddDamageHole(visualRegions[index]);
            coordinator.SpawnUnsupportedCluster(
                this,
                groupBounds,
                direction,
                material,
                seed);
            detachedClusterCount++;
            EnsureLocalizedDamageRepresentation();
            UpdateDamageVisuals();
            RebuildCollisionProxies();
            RefreshIntegrity();
        }

        void EvaluateStructuralSupport()
        {
            for (int index = 0; index < structuralCells.Count; index++)
                structuralCells[index].supported = false;
            for (int level = 0; level < gridLevels; level++)
            for (int z = 0; z < gridColumns; z++)
            for (int x = 0; x < gridColumns; x++)
            {
                StructuralCell cell = FindCell(x, z, level);
                if (cell == null || !cell.active || cell.pendingDetach)
                    continue;
                if (level == 0)
                {
                    cell.supported = true;
                    continue;
                }
                float supportScore = IsSupported(x, z, level - 1) ? 1f : 0f;
                float transfer = coordinator != null
                    ? coordinator.NeighbourSupportTransfer
                    : 0.22f;
                if (IsSupported(x - 1, z, level - 1)) supportScore += transfer;
                if (IsSupported(x + 1, z, level - 1)) supportScore += transfer;
                if (IsSupported(x, z - 1, level - 1)) supportScore += transfer;
                if (IsSupported(x, z + 1, level - 1)) supportScore += transfer;
                float threshold = cell.role == StructuralRole.Core
                    ? 0.58f
                    : cell.role == StructuralRole.Column
                        ? 0.50f
                        : 0.34f;
                cell.supported = supportScore >= threshold;
            }
        }

        bool IsSupported(int x, int z, int level)
        {
            StructuralCell cell = FindCell(x, z, level);
            return cell != null && cell.active && !cell.pendingDetach &&
                   cell.supported;
        }

        StructuralCell FindCell(int x, int z, int level)
        {
            if (x < 0 || x >= gridColumns || z < 0 || z >= gridColumns ||
                level < 0 || level >= gridLevels)
            {
                return null;
            }
            int index = level * gridColumns * gridColumns + z * gridColumns + x;
            return index >= 0 && index < structuralCells.Count
                ? structuralCells[index]
                : null;
        }

        static int ManhattanDistance(StructuralCell first, StructuralCell second)
        {
            return Mathf.Abs(first.x - second.x) +
                   Mathf.Abs(first.z - second.z) +
                   Mathf.Abs(first.level - second.level);
        }

        int CountActiveCells()
        {
            int count = 0;
            for (int index = 0; index < structuralCells.Count; index++)
                if (structuralCells[index].active)
                    count++;
            return count;
        }

        void RefreshIntegrity()
        {
            if (structuralCells.Count == 0)
            {
                integrity = 0f;
                return;
            }
            float remaining = 0f;
            float total = 0f;
            for (int index = 0; index < structuralCells.Count; index++)
            {
                StructuralCell cell = structuralCells[index];
                total += cell.maximumIntegrity;
                if (cell.active)
                    remaining += cell.integrity;
            }
            integrity = total <= 0.01f
                ? 0f
                : maximumIntegrity * Mathf.Clamp01(remaining / total);
        }

        void AddDamageHole(Bounds hole)
        {
            hole.Expand(new Vector3(0.4f, 0.25f, 0.4f));
            for (int index = 0; index < damageHoles.Count; index++)
            {
                Bounds expanded = damageHoles[index];
                expanded.Expand(Mathf.Max(1f, hole.extents.magnitude * 0.18f));
                if (!expanded.Intersects(hole))
                    continue;
                Bounds merged = damageHoles[index];
                merged.Encapsulate(hole);
                damageHoles[index] = merged;
                return;
            }
            int maximum = Mathf.Min(
                8,
                coordinator != null
                    ? coordinator.MaximumVisibleStructuralHoles
                    : 8);
            if (damageHoles.Count < maximum)
            {
                damageHoles.Add(hole);
                return;
            }
            int nearest = 0;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < damageHoles.Count; index++)
            {
                float distance = (damageHoles[index].center - hole.center).sqrMagnitude;
                if (distance >= nearestDistance)
                    continue;
                nearestDistance = distance;
                nearest = index;
            }
            Bounds combined = damageHoles[nearest];
            combined.Encapsulate(hole);
            damageHoles[nearest] = combined;
        }

        void EnsureLocalizedDamageRepresentation()
        {
            if (damageVisualRoot != null || coordinator == null)
                return;
            var root = new GameObject(
                "LocalizedDamageShell_局部缺口_" + stableId);
            damageVisualRoot = root.transform;
            damageVisualRoot.SetParent(coordinator.transform, true);
            damageVisualRoot.position = Vector3.zero;
            damageVisualRoot.rotation = Quaternion.identity;
            damageVisualRoot.localScale = Vector3.one;
            int created = coordinator.CreateDamageRendererCopies(
                originalRenderers,
                damageVisualRoot,
                damageRenderers,
                damageMaterials,
                damageMeshes);
            if (created <= 0)
            {
                DestroyRuntimeRoot(ref damageVisualRoot);
                return;
            }
            for (int index = 0; index < originalRenderers.Length; index++)
                if (originalRenderers[index] != null)
                    originalRenderers[index].enabled = false;
            UrbanRuntimeResourceOwner owner =
                root.AddComponent<UrbanRuntimeResourceOwner>();
            owner.Configure(damageMaterials, damageMeshes);
        }

        void UpdateDamageVisuals()
        {
            for (int rendererIndex = 0;
                 rendererIndex < damageRenderers.Count;
                 rendererIndex++)
            {
                Renderer renderer = damageRenderers[rendererIndex];
                if (renderer == null)
                    continue;
                var properties = new MaterialPropertyBlock();
                properties.SetFloat("_DamageMode", 1f);
                properties.SetFloat("_DamageLocalSpace", 0f);
                properties.SetFloat("_DamageHoleCount", damageHoles.Count);
                for (int holeIndex = 0; holeIndex < 8; holeIndex++)
                {
                    Bounds hole = holeIndex < damageHoles.Count
                        ? damageHoles[holeIndex]
                        : new Bounds(Vector3.zero, Vector3.zero);
                    properties.SetVector(
                        "_DamageHole" + holeIndex,
                        new Vector4(hole.center.x, hole.center.y, hole.center.z, 1f));
                    properties.SetVector(
                        "_DamageExtent" + holeIndex,
                        new Vector4(
                            hole.extents.x * 1.02f,
                            hole.extents.y * 1.02f,
                            hole.extents.z * 1.02f,
                            0f));
                }
                renderer.SetPropertyBlock(properties);
            }
        }

        void RebuildDamageCavityInterior()
        {
            DestroyRuntimeRoot(ref damageInteriorRoot);
            if (coordinator == null || damageVisualRoot == null ||
                damageHoles.Count == 0)
            {
                return;
            }
            damageInteriorRoot = coordinator.CreateDamageCavityInterior(
                originalRenderers,
                DestructionBounds,
                damageHoles,
                damageVisualRoot,
                stableId);
        }

        void RebuildCollisionProxies()
        {
            if (coordinator == null)
                return;
            for (int index = 0; index < originalColliders.Length; index++)
                if (originalColliders[index] != null)
                    originalColliders[index].enabled = false;
            DestroyRuntimeRoot(ref collisionProxyRoot);

            var root = new GameObject(
                "StructuralCollisionProxy_局部结构_" + stableId);
            collisionProxyRoot = root.transform;
            collisionProxyRoot.SetParent(coordinator.transform, true);
            collisionProxyRoot.position = Vector3.zero;
            collisionProxyRoot.rotation = Quaternion.identity;
            collisionProxyRoot.localScale = Vector3.one;

            for (int z = 0; z < gridColumns; z++)
            for (int x = 0; x < gridColumns; x++)
            {
                int runStart = -1;
                for (int level = 0; level <= gridLevels; level++)
                {
                    bool active = level < gridLevels &&
                                  FindCell(x, z, level)?.active == true;
                    if (active && runStart < 0)
                    {
                        runStart = level;
                        continue;
                    }
                    if (active || runStart < 0)
                        continue;
                    int runEnd = level - 1;
                    Bounds runBounds = FindCell(x, z, runStart).bounds;
                    for (int run = runStart + 1; run <= runEnd; run++)
                        runBounds.Encapsulate(FindCell(x, z, run).bounds);
                    var proxyObject = new GameObject(
                        "SupportColumn_" + x + "_" + z + "_" +
                        runStart + "_" + runEnd);
                    proxyObject.transform.SetParent(collisionProxyRoot, true);
                    proxyObject.transform.position = runBounds.center;
                    proxyObject.transform.rotation = Quaternion.identity;
                    BoxCollider collider = proxyObject.AddComponent<BoxCollider>();
                    collider.center = Vector3.zero;
                    collider.size = Vector3.Scale(
                        runBounds.size,
                        new Vector3(0.94f, 0.98f, 0.94f));
                    UrbanStructuralColliderProxy proxy =
                        proxyObject.AddComponent<UrbanStructuralColliderProxy>();
                    proxy.Configure(this);
                    runStart = -1;
                }
            }
        }

        void Collapse(in UrbanDamageRequest request)
        {
            if (collapsed)
                return;
            Bounds collapseBounds = DestructionBounds;
            lastCollapseImpactPoint = request.point;
            lastCollapseCutHeight = Mathf.Clamp(
                request.point.y,
                collapseBounds.min.y + collapseBounds.size.y * 0.16f,
                collapseBounds.min.y + collapseBounds.size.y * 0.84f);
            lastCollapseWasEnergyBlade = true;
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

            FinalizeCollapse(collapseBounds);
        }

        void FinalizeCollapse(Bounds collapseBounds)
        {
            collapsed = true;
            pendingGroups.Clear();
            for (int index = 0; index < originalRenderers.Length; index++)
                if (originalRenderers[index] != null)
                    originalRenderers[index].enabled = false;
            for (int index = 0; index < originalColliders.Length; index++)
                if (originalColliders[index] != null)
                    originalColliders[index].enabled = false;
            DestroyRuntimeRoot(ref damageVisualRoot);
            DestroyRuntimeRoot(ref collisionProxyRoot);
            DestroyBreachVisuals();
            UrbanDestructionWorld.BreakDecorationsNear(collapseBounds);
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

        public void DebugApplyEnergyBlade(Vector3 point, Vector3 normal)
        {
            ApplyUrbanDamage(new UrbanDamageRequest(
                point,
                normal,
                -normal,
                180f,
                30f,
                180f,
                UrbanDamageKind.EnergyBlade,
                null));
        }

        static Bounds CombinedBounds(IReadOnlyList<Bounds> source)
        {
            if (source == null || source.Count == 0)
                return new Bounds(Vector3.zero, Vector3.zero);
            Bounds result = source[0];
            for (int index = 1; index < source.Count; index++)
                result.Encapsulate(source[index]);
            return result;
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
            float distance = float.PositiveInfinity;
            ResolvePositiveExit(origin.x, bounds.min.x, bounds.max.x,
                direction.x, ref distance);
            ResolvePositiveExit(origin.y, bounds.min.y, bounds.max.y,
                direction.y, ref distance);
            ResolvePositiveExit(origin.z, bounds.min.z, bounds.max.z,
                direction.z, ref distance);
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
            DestroyRuntimeRoot(ref breachVisualRoot);
        }

        static void DestroyRuntimeRoot(ref Transform target)
        {
            if (target == null)
                return;
            GameObject root = target.gameObject;
            target = null;
            root.SetActive(false);
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
}
