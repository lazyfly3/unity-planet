using System;
using System.Collections;
using System.Collections.Generic;
using ModularAssembly;
using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.EDPCG;
using UnityPlanet.SpaceStation.Skills;

namespace UnityPlanet.ModularAssembly
{
    public enum CombatTestMode
    {
        Duel,
        Horde
    }

    public enum VehicleCombatTeam
    {
        Neutral,
        Player,
        Enemy
    }

    [DisallowMultipleComponent]
    public sealed class VehicleCombatTeamMarker : MonoBehaviour
    {
        [SerializeField] VehicleCombatTeam team;

        public VehicleCombatTeam Team
        {
            get => team;
            set => team = value;
        }
    }

    public static class VehicleCombatTeamUtility
    {
        public static VehicleCombatTeam Resolve(Transform target)
        {
            VehicleCombatTeamMarker marker = target == null
                ? null
                : target.GetComponentInParent<VehicleCombatTeamMarker>();
            return marker != null ? marker.Team : VehicleCombatTeam.Neutral;
        }

        public static bool AreFriendly(Transform source, Transform target)
        {
            VehicleCombatTeam sourceTeam = Resolve(source);
            return sourceTeam != VehicleCombatTeam.Neutral &&
                   sourceTeam == Resolve(target);
        }

        public static bool AreFriendly(GameObject source, Transform target)
        {
            return source != null && AreFriendly(source.transform, target);
        }

        public static VehicleCombatTeamMarker SetTeam(
            GameObject root,
            VehicleCombatTeam team)
        {
            if (root == null)
                return null;
            VehicleCombatTeamMarker marker =
                root.GetComponent<VehicleCombatTeamMarker>() ??
                root.AddComponent<VehicleCombatTeamMarker>();
            marker.Team = team;
            return marker;
        }
    }

    public enum HordeEnemyRole
    {
        Interceptor,
        Striker,
        Gunship
    }

    public enum HordeEnemyAttackKind
    {
        Suicide,
        Ranged
    }

    public enum HordeFormationKind
    {
        V,
        LineAbreast,
        Pincer,
        Echelon
    }

    public enum HordeSpawnRuleKind
    {
        OpeningSweep,
        InterceptorScreen,
        StrikerEscort,
        PincerPressure,
        GunshipCommand
    }

    /// <summary>
    /// Keeps a tactically positioned enemy from becoming permanently detached
    /// from the player. Re-engagement still uses the navigation graph and does
    /// not grant permission to fly through city geometry or bypass attack caps.
    /// </summary>
    public static class HordeReengagementPolicy
    {
        public const float MinimumSpawnAgeSeconds = 3f;
        public const float LostContactSeconds = 6f;
        public const float HardRecallDistance = 520f;
        public const float SuicideContactDistance = 72f;
        public const float RangedContactDistance = 420f;

        public static bool ShouldBegin(
            float secondsSinceSpawn,
            float secondsSinceContact,
            float playerDistance,
            bool attackInProgress,
            bool attackRecovering)
        {
            if (attackInProgress || attackRecovering ||
                secondsSinceSpawn < MinimumSpawnAgeSeconds)
            {
                return false;
            }
            return playerDistance >= HardRecallDistance ||
                   secondsSinceContact >= LostContactSeconds;
        }

        public static bool HasReestablishedContact(
            HordeEnemyAttackKind attackKind,
            float playerDistance,
            bool hasClearCorridor,
            bool attackInProgress)
        {
            if (attackInProgress)
                return true;
            float contactDistance = attackKind == HordeEnemyAttackKind.Suicide
                ? SuicideContactDistance
                : RangedContactDistance;
            return hasClearCorridor && playerDistance <= contactDistance;
        }

        public static Vector3 ResolveDestination(
            HordeEnemyProfile profile,
            Vector3 enemyPosition,
            Vector3 playerPosition,
            Vector3 playerVelocity,
            int stableIndex)
        {
            HordeEnemyProfile safeProfile = profile ??
                HordeEnemyProfile.ForRole(HordeEnemyRole.Interceptor);
            Vector3 predictedPlayer = playerPosition +
                                      Vector3.ProjectOnPlane(
                                          playerVelocity,
                                          Vector3.up) * 0.35f;
            Vector3 outward = Vector3.ProjectOnPlane(
                enemyPosition - predictedPlayer,
                Vector3.up);
            if (outward.sqrMagnitude < 0.01f)
            {
                float angle = stableIndex * 137.5f * Mathf.Deg2Rad;
                outward = new Vector3(
                    Mathf.Sin(angle),
                    0f,
                    Mathf.Cos(angle));
            }
            outward.Normalize();
            Vector3 lateral = Vector3.Cross(Vector3.up, outward);
            if (lateral.sqrMagnitude < 0.01f)
                lateral = Vector3.right;
            lateral.Normalize();
            float side = (stableIndex & 1) == 0 ? 1f : -1f;
            Vector3 flankDirection =
                (outward * 0.32f + lateral * side * 0.95f).normalized;
            float standoff = safeProfile.attackKind ==
                             HordeEnemyAttackKind.Suicide
                ? 62f
                : Mathf.Clamp(
                    (safeProfile.preferredMinimumRange +
                     safeProfile.preferredMaximumRange) * 0.5f,
                    95f,
                    165f);
            float altitude = safeProfile.role == HordeEnemyRole.Gunship
                ? 28f
                : safeProfile.attackKind == HordeEnemyAttackKind.Suicide
                    ? 20f
                    : 16f;
            return predictedPlayer + flankDirection * standoff +
                   Vector3.up * altitude;
        }
    }

    /// <summary>
    /// Ordinary horde weapons may be stopped by city cover, but cannot alter
    /// its structural graph. Boss attacks and explicitly armed player skills
    /// use their own authorized demolition paths.
    /// </summary>
    public static class HordeUrbanCombatPolicy
    {
        public const bool CanDamageUrbanStructures = false;
    }

    [Serializable]
    public sealed class HordeEnemyProfile
    {
        public HordeEnemyRole role;
        public float maximumHealth;
        public float massKg;
        public float maximumSpeed;
        public float maximumAcceleration;
        public float preferredMinimumRange;
        public float preferredMaximumRange;
        public float shotDamage;
        public int gunCount;
        public HordeEnemyAttackKind attackKind;
        public float detonationDamage;
        public float detonationRadius;
        public int threatCost;
        public int attackTokenCost;
        public Color hullColor;

        public static HordeEnemyProfile ForRole(HordeEnemyRole role)
        {
            switch (role)
            {
                case HordeEnemyRole.Striker:
                    return new HordeEnemyProfile
                    {
                        role = role,
                        maximumHealth = 420f,
                        massKg = 1200f,
                        maximumSpeed = 58f,
                        maximumAcceleration = 14f,
                        preferredMinimumRange = 90f,
                        preferredMaximumRange = 130f,
                        shotDamage = 10f,
                        gunCount = 1,
                        attackKind = HordeEnemyAttackKind.Ranged,
                        threatCost = 2,
                        attackTokenCost = 1,
                        hullColor = new Color(0.72f, 0.18f, 0.08f)
                    };
                case HordeEnemyRole.Gunship:
                    return new HordeEnemyProfile
                    {
                        role = role,
                        maximumHealth = 700f,
                        massKg = 1800f,
                        maximumSpeed = 48f,
                        maximumAcceleration = 10f,
                        preferredMinimumRange = 130f,
                        preferredMaximumRange = 180f,
                        shotDamage = 12f,
                        gunCount = 2,
                        attackKind = HordeEnemyAttackKind.Ranged,
                        threatCost = 3,
                        attackTokenCost = 2,
                        hullColor = new Color(0.55f, 0.10f, 0.06f)
                    };
                default:
                    return new HordeEnemyProfile
                    {
                        role = HordeEnemyRole.Interceptor,
                        maximumHealth = 220f,
                        massKg = 850f,
                        maximumSpeed = 70f,
                        maximumAcceleration = 18f,
                        preferredMinimumRange = 0f,
                        preferredMaximumRange = 8f,
                        shotDamage = 0f,
                        gunCount = 0,
                        attackKind = HordeEnemyAttackKind.Suicide,
                        detonationDamage = 110f,
                        detonationRadius = 11f,
                        threatCost = 1,
                        attackTokenCost = 1,
                        hullColor = new Color(0.86f, 0.27f, 0.08f)
                    };
            }
        }
    }

    public sealed class HordeAirNavigationService
    {
        const int AltitudeLayers = 3;
        const int RadiusLayers = 3;
        const int SectorCount = 16;
        const int MaximumNodes =
            AltitudeLayers * RadiusLayers * SectorCount;
        const float CorridorRadius = 5f;

        sealed class Node
        {
            public Vector3 Position;
            public bool Valid;
            public readonly int[] Neighbors = new int[6];
            public int NeighborCount;
        }

        readonly Node[] nodes = new Node[MaximumNodes];
        readonly RaycastHit[] rayHits = new RaycastHit[64];
        readonly Collider[] overlapHits = new Collider[64];
        readonly float[] scores = new float[MaximumNodes];
        readonly float[] estimated = new float[MaximumNodes];
        readonly int[] previous = new int[MaximumNodes];
        readonly bool[] opened = new bool[MaximumNodes];
        readonly bool[] closed = new bool[MaximumNodes];
        readonly int[] reversePath = new int[MaximumNodes];
        Vector3 battleCenter;
        float warningRadius;
        Transform playerRoot;
        int validNodeCount;

        public bool IsReady { get; private set; }
        public string FailureReason { get; private set; } = string.Empty;
        public int ValidNodeCount => validNodeCount;

        public IEnumerator Build(
            Vector3 center,
            float arenaWarningRadius,
            Transform player,
            Action<float, string> progress)
        {
            IsReady = false;
            FailureReason = string.Empty;
            battleCenter = center;
            warningRadius = Mathf.Max(300f, arenaWarningRadius);
            playerRoot = player;
            validNodeCount = 0;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            for (int altitude = 0; altitude < AltitudeLayers; altitude++)
            for (int radius = 0; radius < RadiusLayers; radius++)
            for (int sector = 0; sector < SectorCount; sector++)
            {
                int index = Index(altitude, radius, sector);
                Node node = nodes[index] ?? (nodes[index] = new Node());
                node.Valid = false;
                node.NeighborCount = 0;
                float angle = sector * Mathf.PI * 2f / SectorCount;
                float radialDistance = ResolveRadius(radius);
                Vector3 horizontal = new Vector3(
                    Mathf.Sin(angle),
                    0f,
                    Mathf.Cos(angle)) * radialDistance;
                Vector3 sample = battleCenter + horizontal;
                if (TrySampleGround(sample.x, sample.z, out float ground))
                {
                    float height = altitude == 0
                        ? 60f
                        : altitude == 1 ? 110f : 160f;
                    Vector3 candidate = new Vector3(
                        sample.x,
                        ground + height,
                        sample.z);
                    if (!HasStaticOccupant(candidate, CorridorRadius))
                    {
                        node.Position = candidate;
                        node.Valid = true;
                        validNodeCount++;
                    }
                }

                progress?.Invoke(
                    0.55f * (index + 1f) / MaximumNodes,
                    "正在采样空中航路");
                if (stopwatch.Elapsed.TotalMilliseconds >= 2.0)
                {
                    yield return null;
                    stopwatch.Restart();
                }
            }

            for (int altitude = 0; altitude < AltitudeLayers; altitude++)
            for (int radius = 0; radius < RadiusLayers; radius++)
            for (int sector = 0; sector < SectorCount; sector++)
            {
                int index = Index(altitude, radius, sector);
                Node node = nodes[index];
                if (node == null || !node.Valid)
                    continue;
                TryConnect(index, altitude, radius, WrapSector(sector - 1));
                TryConnect(index, altitude, radius, WrapSector(sector + 1));
                if (radius > 0)
                    TryConnect(index, altitude, radius - 1, sector);
                if (radius + 1 < RadiusLayers)
                    TryConnect(index, altitude, radius + 1, sector);
                if (altitude > 0)
                    TryConnect(index, altitude - 1, radius, sector);
                if (altitude + 1 < AltitudeLayers)
                    TryConnect(index, altitude + 1, radius, sector);

                progress?.Invoke(
                    0.55f + 0.45f * (index + 1f) / MaximumNodes,
                    "正在验证航路通道");
                if (stopwatch.Elapsed.TotalMilliseconds >= 2.0)
                {
                    yield return null;
                    stopwatch.Restart();
                }
            }

            IsReady = validNodeCount >= 12 && HasConnectedRoute();
            if (!IsReady)
            {
                FailureReason =
                    "空中航路图没有足够的安全节点或连通通道，割草战斗未启动。";
            }
            progress?.Invoke(1f, IsReady ? "空中航路已就绪" : FailureReason);
        }

        public bool TrySampleGround(float worldX, float worldZ, out float height)
        {
            Vector3 origin = new Vector3(
                worldX,
                battleCenter.y + 800f,
                worldZ);
            int count = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                rayHits,
                1800f,
                ~0,
                QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            height = 0f;
            bool found = false;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = rayHits[index];
                if (hit.collider == null ||
                    !IsStaticEnvironment(hit.collider) ||
                    hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                height = hit.point.y;
                found = true;
            }
            if (found)
                return true;

            PlanetEnvironmentSample sample = PlanetEnvironmentRuntime.Sample(
                new Vector3(worldX, battleCenter.y, worldZ),
                Time.timeAsDouble);
            if (!sample.hasSurface)
                return false;
            height = sample.surfacePoint.y;
            return !float.IsNaN(height) && !float.IsInfinity(height);
        }

        public bool HasStaticClearCorridor(Vector3 start, Vector3 end)
        {
            Vector3 delta = end - start;
            float distance = delta.magnitude;
            if (distance <= 0.01f)
                return true;
            int count = Physics.SphereCastNonAlloc(
                start,
                CorridorRadius,
                delta / distance,
                rayHits,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider collider = rayHits[index].collider;
                if (collider != null && IsStaticEnvironment(collider))
                    return false;
            }
            return true;
        }

        public bool FindPath(
            Vector3 start,
            Vector3 destination,
            List<Vector3> result)
        {
            result.Clear();
            if (!IsReady)
                return false;
            if (HasStaticClearCorridor(start, destination))
            {
                result.Add(destination);
                return true;
            }

            int startNode = NearestNode(start);
            int goalNode = NearestNode(destination);
            if (startNode < 0 || goalNode < 0)
                return false;
            for (int index = 0; index < MaximumNodes; index++)
            {
                scores[index] = float.PositiveInfinity;
                estimated[index] = float.PositiveInfinity;
                previous[index] = -1;
                opened[index] = false;
                closed[index] = false;
            }
            scores[startNode] = 0f;
            estimated[startNode] = Vector3.Distance(
                nodes[startNode].Position,
                nodes[goalNode].Position);
            opened[startNode] = true;

            while (true)
            {
                int current = -1;
                float best = float.PositiveInfinity;
                for (int index = 0; index < MaximumNodes; index++)
                {
                    if (!opened[index] || estimated[index] >= best)
                        continue;
                    current = index;
                    best = estimated[index];
                }
                if (current < 0)
                    return false;
                if (current == goalNode)
                    break;
                opened[current] = false;
                closed[current] = true;
                Node node = nodes[current];
                for (int edge = 0; edge < node.NeighborCount; edge++)
                {
                    int next = node.Neighbors[edge];
                    if (closed[next])
                        continue;
                    float candidate = scores[current] + Vector3.Distance(
                        node.Position,
                        nodes[next].Position);
                    if (candidate >= scores[next])
                        continue;
                    previous[next] = current;
                    scores[next] = candidate;
                    estimated[next] = candidate + Vector3.Distance(
                        nodes[next].Position,
                        nodes[goalNode].Position);
                    opened[next] = true;
                }
            }

            int count = 0;
            for (int current = goalNode;
                 current >= 0 && count < reversePath.Length;
                 current = previous[current])
            {
                reversePath[count++] = current;
                if (current == startNode)
                    break;
            }
            for (int index = count - 1; index >= 0; index--)
            {
                Vector3 point = nodes[reversePath[index]].Position;
                if ((point - start).sqrMagnitude > 16f &&
                    (result.Count > 0 ||
                     HasStaticClearCorridor(start, point)))
                    result.Add(point);
            }
            Vector3 last = result.Count > 0
                ? result[result.Count - 1]
                : start;
            if (!HasStaticClearCorridor(last, destination))
            {
                result.Clear();
                return false;
            }
            result.Add(destination);
            return result.Count > 0;
        }

        bool HasStaticOccupant(Vector3 point, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(
                point,
                radius,
                overlapHits,
                ~0,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider collider = overlapHits[index];
                if (collider != null && IsStaticEnvironment(collider))
                    return true;
            }
            return false;
        }

        bool IsStaticEnvironment(Collider collider)
        {
            return collider != null &&
                   !collider.isTrigger &&
                   collider.attachedRigidbody == null &&
                   (playerRoot == null ||
                    !collider.transform.IsChildOf(playerRoot));
        }

        void TryConnect(
            int source,
            int altitude,
            int radius,
            int sector)
        {
            int target = Index(altitude, radius, sector);
            Node from = nodes[source];
            Node to = nodes[target];
            if (to == null || !to.Valid ||
                ContainsNeighbor(from, target) ||
                !HasStaticClearCorridor(from.Position, to.Position))
                return;
            AddNeighbor(from, target);
            AddNeighbor(to, source);
        }

        static bool ContainsNeighbor(Node node, int candidate)
        {
            for (int index = 0; index < node.NeighborCount; index++)
                if (node.Neighbors[index] == candidate)
                    return true;
            return false;
        }

        static void AddNeighbor(Node node, int target)
        {
            if (node.NeighborCount < node.Neighbors.Length)
                node.Neighbors[node.NeighborCount++] = target;
        }

        bool HasConnectedRoute()
        {
            int seed = -1;
            for (int index = 0; index < MaximumNodes; index++)
            {
                if (nodes[index] != null &&
                    nodes[index].Valid &&
                    nodes[index].NeighborCount > 0)
                {
                    seed = index;
                    break;
                }
            }
            if (seed < 0)
                return false;
            var queue = new Queue<int>();
            var visited = new bool[MaximumNodes];
            queue.Enqueue(seed);
            visited[seed] = true;
            int connected = 0;
            while (queue.Count > 0)
            {
                Node node = nodes[queue.Dequeue()];
                connected++;
                for (int edge = 0; edge < node.NeighborCount; edge++)
                {
                    int next = node.Neighbors[edge];
                    if (visited[next])
                        continue;
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }
            return connected >= Mathf.Min(12, validNodeCount);
        }

        int NearestNode(Vector3 point)
        {
            int result = -1;
            float nearest = float.PositiveInfinity;
            for (int index = 0; index < MaximumNodes; index++)
            {
                Node node = nodes[index];
                if (node == null || !node.Valid)
                    continue;
                float distance = (node.Position - point).sqrMagnitude;
                if (distance >= nearest)
                    continue;
                nearest = distance;
                result = index;
            }
            return result;
        }

        float ResolveRadius(int radiusLayer)
        {
            float fraction = radiusLayer == 0
                ? 0.22f
                : radiusLayer == 1 ? 0.45f : 0.68f;
            return Mathf.Clamp(warningRadius * fraction, 120f, 820f);
        }

        static int Index(int altitude, int radius, int sector)
        {
            return (altitude * RadiusLayers + radius) *
                   SectorCount + WrapSector(sector);
        }

        static int WrapSector(int sector)
        {
            sector %= SectorCount;
            return sector < 0 ? sector + SectorCount : sector;
        }
    }

    [DisallowMultipleComponent]
    public sealed class HordeCombatDirector : MonoBehaviour
    {
        struct QueuedSpawn
        {
            public HordeEnemyRole Role;
            public HordeFormationKind Formation;
            public int Sector;
            public int MemberIndex;
            public int MemberCount;
            public int Attempts;
            public int TotalAttempts;
            public float ReadyAt;
            public string RosterMemberId;
            public int RosterIndex;
            public bool EnvironmentalPursuer;
        }

        const int LegacyPoolSize = 10;
        const int EdpcgMaximumPoolSize = 28;
        const int MaximumSpawnAttempts = 8;
        const int SectorCount = 7;
        const float SectorCooldownSeconds = 8f;
        const float SharedAttackCooldown = 0.45f;
        public const float SessionDurationSeconds = 120f;
        public const float FinalClearStartSeconds = 108f;
        static readonly int[] FrontSectors = { -1, 0, 1 };
        static readonly int[] WideSectors = { -2, -1, 0, 1, 2 };
        static readonly int[] FlankSectors = { -3, -2, 2, 3 };
        readonly List<HordeEnemyVehicle> pool =
            new List<HordeEnemyVehicle>(EdpcgMaximumPoolSize);
        readonly List<HordeEnemyVehicle> active =
            new List<HordeEnemyVehicle>(EdpcgMaximumPoolSize);
        readonly Queue<QueuedSpawn> spawnQueue =
            new Queue<QueuedSpawn>(24);
        readonly List<Vector3> plannedIngresses =
            new List<Vector3>(8);
        readonly List<Vector3> spawnPathProbe =
            new List<Vector3>(24);
        readonly List<Vector3> semanticRouteProbe =
            new List<Vector3>(24);
        readonly List<Vector3> semanticSegmentProbe =
            new List<Vector3>(24);
        readonly Collider[] spawnOverlaps = new Collider[32];
        readonly float[] sectorReadyAt = new float[SectorCount];
        CombatTestController owner;
        WeaponVisualPool visuals;
        WeaponProjectilePool projectiles;
        Rigidbody playerBody;
        HordeAirNavigationService navigation;
        EdpcgEncounterRuntime edpcg;
        System.Random random;
        Vector3 battleCenter;
        float warningRadius;
        float elapsed;
        float nextSpawnAt;
        float nextAttackAt;
        int attackTokens;
        int seed;
        int sessionId;
        int spawnSequence;
        int killsAtLastDecision;
        int lastSector = -1;
        HordeFormationKind lastFormation = (HordeFormationKind)(-1);
        HordeSpawnRuleKind lastRule = HordeSpawnRuleKind.OpeningSweep;
        bool running;
        bool finalClear;
        bool objectiveDrivenSession;

        float EncounterClock => objectiveDrivenSession
            ? Mathf.Min(elapsed, FinalClearStartSeconds - 0.01f)
            : elapsed;

        public bool PreparationValid { get; private set; }
        public string PreparationError { get; private set; } = string.Empty;
        public bool NavigationReady => navigation != null && navigation.IsReady;
        public string NavigationError => navigation?.FailureReason ?? string.Empty;
        public float Elapsed => elapsed;
        public float RemainingSeconds =>
            Mathf.Max(0f, SessionDurationSeconds - elapsed);
        public int Kills { get; private set; }
        public int Escaped { get; private set; }
        public int PeakActive { get; private set; }
        public int QueuedCount => spawnQueue.Count;
        public int SessionId => sessionId;
        public int PlannedIngressCount => plannedIngresses.Count;
        public HordeSpawnRuleKind CurrentSpawnRule => lastRule;
        public bool IsFinalClear => finalClear;
        public bool IsRunning => running;
        public EdpcgEncounterRuntime EdpcgRuntime => edpcg;
        public int AttackTokensUsed => attackTokens;
        public bool IsFinished =>
            running && !objectiveDrivenSession &&
            elapsed >= SessionDurationSeconds;

        public int AliveCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < active.Count; index++)
                    if (active[index] != null && active[index].IsCombatCapable)
                        count++;
                return count;
            }
        }

        public int CurrentPhase => edpcg != null
            ? (int)edpcg.Phase + 1
            : PhaseAt(EncounterClock);
        public int CurrentActiveCap => edpcg != null
            ? edpcg.PopulationCap
            : ActiveCapAt(EncounterClock);
        public int StrategyLevel => edpcg != null
            ? edpcg.StrategyLevel
            : Mathf.Clamp(CurrentPhase - 1, 0, 2);
        public float SemanticReplanSeconds => edpcg != null
            ? edpcg.Settings.semanticReplanSeconds
            : 0.5f;
        public float LocalRepairAfterSeconds => edpcg != null
            ? edpcg.Settings.localRepairAfterSeconds
            : 2f;
        public float EvasiveAfterSeconds => edpcg != null
            ? edpcg.Settings.evasiveAfterSeconds
            : 5f;
        public float NavigationRecoveryAfterSeconds => edpcg != null
            ? edpcg.Settings.navigationRecoveryAfterSeconds
            : 12f;

        /// <summary>
        /// Planet missions end when their authored objective is complete, not
        /// when the two-minute combat-test timer expires. In that mode the
        /// director keeps its final wave cadence until the mission controller
        /// explicitly requests a final clear.
        /// </summary>
        public void ConfigureObjectiveDrivenSession(bool enabled)
        {
            objectiveDrivenSession = enabled;
        }

        public void ConfigureEdpcgRuntime(EdpcgEncounterRuntime runtime)
        {
            edpcg = runtime;
        }

        public static int PhaseAt(float seconds)
        {
            return seconds < 45f ? 1 :
                seconds < 90f ? 2 :
                seconds < FinalClearStartSeconds ? 3 : 4;
        }

        public static int ActiveCapAt(float seconds)
        {
            return seconds < 12f ? 3 :
                seconds < 45f ? 4 :
                seconds < 90f ? 7 : 10;
        }

        public static float SpawnIntervalAt(float seconds)
        {
            return seconds < 12f ? 7f :
                seconds < 45f ? 6f :
                seconds < 90f ? 5f :
                seconds < FinalClearStartSeconds ? 4f :
                float.PositiveInfinity;
        }

        public static int ThreatBudgetAt(float seconds)
        {
            return seconds < 12f ? 2 :
                seconds < 45f ? 2 :
                seconds < 90f ? 3 :
                seconds < FinalClearStartSeconds ? 4 : 0;
        }

        public static int ThreatCapAt(float seconds)
        {
            return seconds < 12f ? 3 :
                seconds < 45f ? 5 :
                seconds < 90f ? 10 : 15;
        }

        public IEnumerator Prewarm(
            CombatTestController session,
            WeaponVisualPool effectPool,
            WeaponProjectilePool projectilePool,
            Action<float, string> progress)
        {
            owner = session;
            visuals = effectPool;
            projectiles = projectilePool;
            PreparationValid = false;
            PreparationError = string.Empty;

            // Planet-surface missions reuse this director without installing the
            // combat-test controller or its UI. The owner is only a telemetry
            // sink; both weapon pools remain mandatory.
            if (visuals == null || projectiles == null)
            {
                PreparationError = "割草战斗缺少武器或特效池。";
                yield break;
            }

            int targetPoolSize = edpcg != null && edpcg.Settings != null
                ? Mathf.Clamp(
                    edpcg.Settings.populationCap,
                    LegacyPoolSize,
                    EdpcgMaximumPoolSize)
                : LegacyPoolSize;
            for (int index = pool.Count; index < targetPoolSize; index++)
            {
                GameObject root = new GameObject(
                    "HordeEnemy_Prewarmed_" + index);
                root.transform.SetParent(transform, false);
                root.transform.position = new Vector3(0f, -10000f, 0f);
                HordeEnemyVehicle enemy =
                    root.AddComponent<HordeEnemyVehicle>();
                if (!enemy.Initialize(this, visuals, projectiles, out string error))
                {
                    PreparationError = error;
                    root.SetActive(false);
                    yield break;
                }
                pool.Add(enemy);
                enemy.DeactivateForPool();
                progress?.Invoke(
                    (index + 1f) / targetPoolSize,
                    "正在预创建割草敌机 " + (index + 1) + "/" +
                    targetPoolSize);
                yield return null;
            }

            bool explosionReady = CombatFeedbackController.GetOrCreate()
                .PlayDestruction(
                    new ModuleDestructionFeedbackContext(
                        "$horde-prewarm",
                        new Vector3(0f, -10000f, 0f),
                        Vector3.up,
                        GridModuleCategory.Core,
                        true,
                        new Bounds(Vector3.zero, new Vector3(8f, 3f, 10f)),
                        0));
            CombatTransientRoot.ClearVisuals();
            if (!explosionReady)
            {
                PreparationError =
                    "割草敌机核心爆炸资源没有可用的受支持材质，模式启动已中止。";
                yield break;
            }

            projectiles.Prewarm(64);
            navigation = navigation ?? new HordeAirNavigationService();
            PreparationValid = pool.Count >= targetPoolSize;
            progress?.Invoke(1f, "割草战斗资源已就绪");
        }

        /// <summary>
        /// Supplies the authored/PCG perimeter entrances of a finite arena.
        /// CombatTest does not call this method and keeps its original
        /// player-relative spawning behaviour.
        /// </summary>
        public void ConfigurePlannedIngresses(
            IEnumerable<Vector3> worldPositions)
        {
            plannedIngresses.Clear();
            if (worldPositions == null)
                return;
            foreach (Vector3 position in worldPositions)
            {
                if (float.IsNaN(position.x) || float.IsNaN(position.y) ||
                    float.IsNaN(position.z) || float.IsInfinity(position.x) ||
                    float.IsInfinity(position.y) || float.IsInfinity(position.z))
                {
                    continue;
                }
                plannedIngresses.Add(position);
            }
        }

        public static int SelectPlannedIngressIndex(
            IReadOnlyList<Vector3> ingresses,
            Vector3 playerPosition,
            Vector3 playerForward,
            int tacticalSector,
            int attempt)
        {
            if (ingresses == null || ingresses.Count == 0)
                return -1;
            Vector3 forward = Vector3.ProjectOnPlane(
                playerForward,
                Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            forward.Normalize();
            float desiredAngle = tacticalSector * 45f;
            int best = 0;
            float bestDelta = float.PositiveInfinity;
            for (int index = 0; index < ingresses.Count; index++)
            {
                Vector3 direction = Vector3.ProjectOnPlane(
                    ingresses[index] - playerPosition,
                    Vector3.up);
                if (direction.sqrMagnitude < 0.01f)
                    continue;
                float angle = Vector3.SignedAngle(
                    forward,
                    direction,
                    Vector3.up);
                float delta = Mathf.Abs(Mathf.DeltaAngle(
                    desiredAngle,
                    angle));
                if (delta >= bestDelta)
                    continue;
                best = index;
                bestDelta = delta;
            }
            int offset = Mathf.Abs(attempt) % ingresses.Count;
            return (best + offset) % ingresses.Count;
        }

        public IEnumerator PrepareNavigation(
            Rigidbody targetBody,
            Vector3 center,
            float arenaWarningRadius,
            Action<float, string> progress)
        {
            playerBody = targetBody;
            battleCenter = center;
            warningRadius = Mathf.Max(300f, arenaWarningRadius);
            navigation = navigation ?? new HordeAirNavigationService();
            yield return navigation.Build(
                battleCenter,
                warningRadius,
                playerBody != null ? playerBody.transform : null,
                progress);
        }

        public void BeginSession(
            Rigidbody targetBody,
            Vector3 center,
            float arenaWarningRadius,
            int deterministicSeed,
            int newSessionId)
        {
            EndSession();
            playerBody = targetBody;
            battleCenter = center;
            warningRadius = Mathf.Max(300f, arenaWarningRadius);
            seed = deterministicSeed;
            sessionId = newSessionId;
            random = new System.Random(seed);
            elapsed = 0f;
            nextSpawnAt = 0f;
            nextAttackAt = 0f;
            attackTokens = 0;
            spawnSequence = 0;
            killsAtLastDecision = 0;
            lastSector = -1;
            lastFormation = (HordeFormationKind)(-1);
            lastRule = HordeSpawnRuleKind.OpeningSweep;
            for (int index = 0; index < sectorReadyAt.Length; index++)
                sectorReadyAt[index] = 0f;
            Kills = 0;
            Escaped = 0;
            PeakActive = 0;
            finalClear = false;
            running = true;
            edpcg?.BeginSession();
        }

        public void Tick(float deltaTime)
        {
            if (!running)
                return;

            for (int index = active.Count - 1; index >= 0; index--)
            {
                HordeEnemyVehicle enemy = active[index];
                if (enemy == null || enemy.ReadyForPool)
                {
                    if (enemy != null)
                        enemy.DeactivateForPool();
                    active.RemoveAt(index);
                }
            }

            elapsed = Mathf.Min(
                objectiveDrivenSession ? 86400f : SessionDurationSeconds,
                elapsed + Mathf.Max(0f, deltaTime));
            float encounterClock = EncounterClock;
            UpdateEdpcgSnapshot();
            edpcg?.Tick(deltaTime);
            if (!objectiveDrivenSession &&
                elapsed >= FinalClearStartSeconds)
            {
                finalClear = true;
                spawnQueue.Clear();
            }
            else if (Time.time >= nextSpawnAt &&
                     (edpcg != null
                         ? edpcg.ShouldSpawn
                         : !InReliefWindow(encounterClock)))
            {
                if (TryQueueSpawnRule())
                {
                    float jitter = random == null
                        ? 1f
                        : Mathf.Lerp(0.9f, 1.1f, (float)random.NextDouble());
                    nextSpawnAt = Time.time +
                                  (edpcg != null
                                      ? edpcg.SpawnInterval
                                      : SpawnIntervalAt(encounterClock)) * jitter;
                }
                else
                {
                    nextSpawnAt = Time.time + 1f;
                }
            }

            int cap = CurrentActiveCap;
            while (!finalClear && spawnQueue.Count > 0 && AliveCount < cap)
            {
                if (!TrySpawnQueued())
                    break;
            }
            PeakActive = Mathf.Max(PeakActive, AliveCount);
        }

        public void EndSession()
        {
            running = false;
            finalClear = false;
            spawnQueue.Clear();
            attackTokens = 0;
            nextAttackAt = 0f;
            for (int index = active.Count - 1; index >= 0; index--)
            {
                if (active[index] != null)
                    active[index].DeactivateForPool();
            }
            active.Clear();
            edpcg?.EndSession();
        }

        public void BeginFinalClear()
        {
            if (!running)
                return;
            finalClear = true;
            spawnQueue.Clear();
        }

        public HordeEnemyVehicle GetAliveEnemy(int aliveIndex)
        {
            int found = 0;
            for (int index = 0; index < active.Count; index++)
            {
                HordeEnemyVehicle enemy = active[index];
                if (enemy == null || !enemy.IsCombatCapable)
                    continue;
                if (found++ == aliveIndex)
                    return enemy;
            }
            return null;
        }

        public Vector3 ResolveTacticalDestination(HordeEnemyVehicle enemy)
        {
            return ResolveTacticalDestination(
                enemy,
                enemy != null ? enemy.PathIntent : EdpcgPathIntent.Probe);
        }

        public Vector3 ResolveTacticalDestination(
            HordeEnemyVehicle enemy,
            EdpcgPathIntent intent)
        {
            if (enemy == null || playerBody == null)
                return battleCenter;
            HordeEnemyProfile profile = enemy.Profile;
            if (intent == EdpcgPathIntent.EnvironmentalTrapCommit &&
                edpcg != null &&
                edpcg.TryAcquireOrMaintainEnvironmentalTrap(
                    enemy,
                    out Vector3 trapDestination))
            {
                return trapDestination;
            }
            if (intent == EdpcgPathIntent.ReacquirePlayer)
            {
                edpcg?.Reservations?.ReleaseOwner(enemy.RosterMemberId);
                if (edpcg == null &&
                    UrbanEnvironmentalFieldDirector.TryResolveEnemyLure(
                        playerBody,
                        enemy.BodyPosition,
                        enemy.SlotIndex,
                        enemy.Role,
                        out Vector3 environmentalLure))
                {
                    return environmentalLure;
                }
                Vector3 reacquireDestination =
                    HordeReengagementPolicy.ResolveDestination(
                        profile,
                        enemy.BodyPosition,
                        playerBody.worldCenterOfMass,
                        playerBody.velocity,
                        enemy.SlotIndex);
                float reacquireCenterDistance = Vector3.Distance(
                    reacquireDestination,
                    battleCenter);
                if (reacquireCenterDistance > warningRadius * 0.78f)
                {
                    reacquireDestination = Vector3.Lerp(
                        reacquireDestination,
                        battleCenter + Vector3.up * 80f,
                        Mathf.InverseLerp(
                            warningRadius * 0.78f,
                            warningRadius,
                            reacquireCenterDistance));
                }
                return reacquireDestination;
            }
            if (profile.attackKind == HordeEnemyAttackKind.Suicide &&
                (intent == EdpcgPathIntent.CommitAttackRun ||
                 intent == EdpcgPathIntent.Intercept))
            {
                float leadSeconds = Mathf.Clamp(
                    Vector3.Distance(
                        enemy.BodyPosition,
                        playerBody.worldCenterOfMass) /
                    Mathf.Max(1f, profile.maximumSpeed),
                    0f,
                    0.65f);
                return playerBody.worldCenterOfMass +
                       playerBody.velocity * leadSeconds;
            }
            if (edpcg != null &&
                edpcg.TryResolveTacticalDestination(
                    enemy,
                    intent,
                    out Vector3 semanticDestination,
                    out _,
                    out _,
                    out _))
            {
                return semanticDestination;
            }
            if (profile.attackKind == HordeEnemyAttackKind.Suicide)
            {
                float suicideAngle = (enemy.SlotIndex * 137.5f +
                                      (int)intent * 29f) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(
                    Mathf.Sin(suicideAngle),
                    0f,
                    Mathf.Cos(suicideAngle)) * 95f;
                return playerBody.worldCenterOfMass + offset +
                       Vector3.up * 20f;
            }
            float distance = (profile.preferredMinimumRange +
                              profile.preferredMaximumRange) * 0.5f;
            float angle = (enemy.SlotIndex * 137.5f +
                           (int)profile.role * 31f) * Mathf.Deg2Rad;
            Vector3 radial = new Vector3(
                Mathf.Sin(angle),
                0f,
                Mathf.Cos(angle));
            Vector3 destination = playerBody.worldCenterOfMass +
                                  radial * distance +
                                  Vector3.up * (profile.role == HordeEnemyRole.Gunship
                                      ? 28f
                                      : 16f);
            float centerDistance = Vector3.Distance(destination, battleCenter);
            if (centerDistance > warningRadius * 0.78f)
            {
                destination = Vector3.Lerp(
                    destination,
                    battleCenter + Vector3.up * 80f,
                    Mathf.InverseLerp(
                        warningRadius * 0.78f,
                        warningRadius,
                        centerDistance));
            }
            return destination;
        }

        public bool TryResolveEnvironmentalTrapCommit(
            HordeEnemyVehicle enemy,
            out Vector3 destination)
        {
            destination = Vector3.zero;
            return edpcg != null && enemy != null &&
                   (enemy.IsEnvironmentalTrapCommitted ||
                    enemy.CanAcceptEnvironmentalTrapCommit) &&
                   edpcg.TryAcquireOrMaintainEnvironmentalTrap(
                       enemy,
                       out destination);
        }

        public void ReleaseEnvironmentalTrapCommit(
            HordeEnemyVehicle enemy,
            string reason)
        {
            edpcg?.ReleaseEnvironmentalTrapCommitment(enemy, reason);
        }

        public bool HasHigherPriorityEnvironmentalCandidate(
            HordeEnemyVehicle candidate)
        {
            if (candidate == null)
                return false;
            int candidatePriority = EnvironmentalPriority(candidate);
            for (int index = 0; index < active.Count; index++)
            {
                HordeEnemyVehicle other = active[index];
                if (other == null || other == candidate ||
                    !other.CanAcceptEnvironmentalTrapCommit ||
                    other.IsEnvironmentalTrapCommitted ||
                    edpcg == null ||
                    !edpcg.CanAttemptEnvironmentalTrap(other))
                {
                    continue;
                }
                if (EnvironmentalPriority(other) > candidatePriority)
                    return true;
            }
            return false;
        }

        static int EnvironmentalPriority(HordeEnemyVehicle enemy)
        {
            if (enemy == null || enemy.Role == HordeEnemyRole.Gunship)
                return 0;
            if (enemy.IsEnvironmentalPursuer)
                return 3;
            return enemy.Role == HordeEnemyRole.Interceptor ? 2 : 1;
        }

        public Vector3 SampleSeparation(HordeEnemyVehicle source)
        {
            if (source == null)
                return Vector3.zero;
            Vector3 result = Vector3.zero;
            Vector3 position = source.BodyPosition;
            for (int index = 0; index < active.Count; index++)
            {
                HordeEnemyVehicle other = active[index];
                if (other == null || other == source || !other.IsCombatCapable)
                    continue;
                Vector3 delta = position - other.BodyPosition;
                float distance = delta.magnitude;
                if (distance <= 0.01f || distance >= 38f)
                    continue;
                result += delta / distance * (1f - distance / 38f);
            }
            return Vector3.ClampMagnitude(result, 1f);
        }

        public bool TryFindPath(
            Vector3 start,
            Vector3 destination,
            List<Vector3> result)
        {
            return navigation != null &&
                   navigation.FindPath(start, destination, result);
        }

        public bool TryFindPath(
            HordeEnemyVehicle enemy,
            Vector3 start,
            Vector3 destination,
            List<Vector3> result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (edpcg == null || enemy == null ||
                !edpcg.TryGetReservedRouteWaypoints(
                    enemy.RosterMemberId,
                    start,
                    semanticRouteProbe))
            {
                return TryFindPath(start, destination, result);
            }
            result.Clear();
            Vector3 segmentStart = start;
            for (int index = 0; index <= semanticRouteProbe.Count; index++)
            {
                Vector3 segmentEnd = index < semanticRouteProbe.Count
                    ? semanticRouteProbe[index]
                    : destination;
                if (Vector3.Distance(segmentStart, segmentEnd) < 8f)
                    continue;
                if (navigation == null ||
                    !navigation.FindPath(
                        segmentStart,
                        segmentEnd,
                        semanticSegmentProbe))
                {
                    result.Clear();
                    return false;
                }
                for (int point = 0; point < semanticSegmentProbe.Count; point++)
                {
                    Vector3 candidate = semanticSegmentProbe[point];
                    if (result.Count == 0 ||
                        Vector3.Distance(result[result.Count - 1], candidate) >= 2f)
                    {
                        result.Add(candidate);
                    }
                }
                segmentStart = segmentEnd;
            }
            return result.Count > 0;
        }

        public bool HasLineOfTravel(Vector3 start, Vector3 end)
        {
            return navigation != null &&
                   navigation.HasStaticClearCorridor(start, end);
        }

        public bool TryGetArrivalWarning(out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;
            if (spawnQueue.Count == 0 || playerBody == null)
                return false;
            QueuedSpawn queued = spawnQueue.Peek();
            if (queued.ReadyAt <= Time.time)
                return false;
            if (plannedIngresses.Count > 0)
            {
                int ingressIndex = SelectPlannedIngressIndex(
                    plannedIngresses,
                    playerBody.worldCenterOfMass,
                    playerBody.transform.forward,
                    queued.Sector,
                    queued.Attempts);
                if (ingressIndex >= 0)
                {
                    worldPosition = plannedIngresses[ingressIndex];
                    return true;
                }
            }
            Vector3 forward = Vector3.ProjectOnPlane(
                playerBody.transform.forward,
                Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            Vector3 radial = Quaternion.AngleAxis(
                queued.Sector * 45f,
                Vector3.up) * forward.normalized;
            worldPosition = playerBody.worldCenterOfMass + radial * 220f;
            return true;
        }

        public bool TryAcquireAttackToken(HordeEnemyVehicle enemy)
        {
            if (!running || enemy == null || enemy.HasAttackToken)
                return false;
            if (edpcg != null && !edpcg.CanGrantAttack(enemy))
                return false;
            if (edpcg != null && IsRoleAttackLimitReached(enemy))
                return false;
            int limit = edpcg != null
                ? edpcg.AttackTokenCap
                : CurrentPhase == 1 ? 1 : 2;
            int cost = enemy.Profile.attackTokenCost;
            if (attackTokens + cost > limit || Time.time < nextAttackAt)
                return false;
            attackTokens += cost;
            nextAttackAt = Time.time + (edpcg != null
                ? edpcg.Settings.sharedAttackCooldownSeconds
                : SharedAttackCooldown);
            enemy.SetAttackToken(true);
            return true;
        }

        public void ReleaseAttackToken(HordeEnemyVehicle enemy)
        {
            if (enemy == null || !enemy.HasAttackToken)
                return;
            attackTokens = Mathf.Max(
                0,
                attackTokens - enemy.Profile.attackTokenCost);
            enemy.SetAttackToken(false);
        }

        bool IsRoleAttackLimitReached(HordeEnemyVehicle candidate)
        {
            int matching = 0;
            for (int index = 0; index < active.Count; index++)
            {
                HordeEnemyVehicle enemy = active[index];
                if (enemy == null || !enemy.IsCombatCapable ||
                    !enemy.HasAttackToken ||
                    enemy.IsSuicide != candidate.IsSuicide)
                {
                    continue;
                }
                matching++;
            }
            int limit = candidate.IsSuicide
                ? edpcg.Settings.suicideCommitCap
                : edpcg.Settings.rangedFireLaneCap;
            return matching >= limit;
        }

        public void NotifyEnemyDeath(
            HordeEnemyVehicle enemy,
            float damage,
            EdpcgEnemyResolutionReason reason =
                EdpcgEnemyResolutionReason.KilledByPlayer)
        {
            ReleaseAttackToken(enemy);
            bool credited = true;
            if (edpcg != null && enemy != null)
            {
                if (!edpcg.ResolveMember(
                        enemy.RosterMemberId,
                        reason,
                        enemy.BodyPosition,
                        out credited))
                {
                    credited = false;
                }
            }
            if (credited)
                Kills++;
            owner?.RecordEnemyDamage(damage);
        }

        public void NotifyEnemyDamaged(float damage)
        {
            owner?.RecordEnemyDamage(damage);
        }

        public void NotifyEnemyEscaped(HordeEnemyVehicle enemy)
        {
            ReleaseAttackToken(enemy);
            if (edpcg != null)
            {
                NotifyEnemyNavigationRecovery(
                    enemy,
                    "left-navigation-envelope");
                return;
            }
            Escaped++;
        }

        public void NotifyEnemyNavigationRecovery(
            HordeEnemyVehicle enemy,
            string reason)
        {
            ReleaseAttackToken(enemy);
            if (edpcg == null || enemy == null ||
                !edpcg.MarkNavigationRecovery(
                    enemy.RosterMemberId,
                    enemy.BodyPosition,
                    reason))
            {
                return;
            }
            QueueRecoveredMember(enemy, 1.5f);
        }

        bool TryQueueSpawnRule()
        {
            if (random == null || playerBody == null || finalClear)
                return false;

            int phase = CurrentPhase;
            float encounterClock = EncounterClock;
            int availableSlots = CurrentActiveCap -
                                 AliveCount - spawnQueue.Count;
            int threatCap = edpcg != null
                ? edpcg.Settings.populationCap * 2
                : ThreatCapAt(encounterClock);
            int threatRoom = threatCap -
                             ActiveThreat() - QueuedThreat();
            if (availableSlots <= 0 || threatRoom <= 0)
                return false;

            int killsSinceDecision = Mathf.Max(0, Kills - killsAtLastDecision);
            int budget = Mathf.Min(
                ThreatBudgetAt(encounterClock),
                threatRoom);
            if (phase >= 2 && killsSinceDecision >= 3 &&
                availableSlots >= 2 && threatRoom > budget)
            {
                budget++;
            }
            killsAtLastDecision = Kills;

            HordeSpawnRuleKind rule = SelectSpawnRule(
                phase,
                budget,
                availableSlots,
                killsSinceDecision);
            var roles = new HordeEnemyRole[4];
            int count = BuildRuleRoles(
                rule,
                phase,
                budget,
                availableSlots,
                roles);
            if (count <= 0 || !TrySelectSector(rule, count, out int sector))
                return false;

            HordeFormationKind formation = SelectFormation(rule, phase);
            float readyAt = Time.time + WarningSecondsFor(rule);
            int queuedMembers = 0;
            for (int index = 0; index < count; index++)
            {
                HordeEnemyRole role = roles[index];
                string rosterMemberId = string.Empty;
                int rosterIndex = -1;
                bool environmentalPursuer = false;
                if (edpcg != null)
                {
                    if (!edpcg.TryReserveNextRosterMember(
                            role,
                            out EdpcgRosterAssignment assignment))
                    {
                        break;
                    }
                    role = assignment.Role;
                    rosterMemberId = assignment.RosterMemberId;
                    rosterIndex = assignment.RosterIndex;
                    environmentalPursuer = assignment.EnvironmentalPursuer;
                }
                spawnQueue.Enqueue(new QueuedSpawn
                {
                    Role = role,
                    Formation = formation,
                    Sector = sector,
                    MemberIndex = queuedMembers,
                    MemberCount = count,
                    Attempts = 0,
                    TotalAttempts = 0,
                    ReadyAt = readyAt,
                    RosterMemberId = rosterMemberId,
                    RosterIndex = rosterIndex,
                    EnvironmentalPursuer = environmentalPursuer
                });
                queuedMembers++;
            }
            if (queuedMembers == 0)
                return false;

            lastRule = rule;
            lastFormation = formation;
            lastSector = sector;
            sectorReadyAt[sector + 3] = Time.time + SectorCooldownSeconds;
            spawnSequence++;
            return true;
        }

        HordeSpawnRuleKind SelectSpawnRule(
            int phase,
            int budget,
            int availableSlots,
            int killsSinceDecision)
        {
            if (EncounterClock < 12f)
                return HordeSpawnRuleKind.OpeningSweep;

            var candidates = new HordeSpawnRuleKind[4];
            var weights = new int[4];
            int count = 0;
            candidates[count] = HordeSpawnRuleKind.InterceptorScreen;
            weights[count++] = 4;

            int strikerLimit = phase >= 3 ? 2 : 1;
            if (budget >= 2 && availableSlots >= 1 &&
                CountRole(HordeEnemyRole.Striker) < strikerLimit)
            {
                candidates[count] = HordeSpawnRuleKind.StrikerEscort;
                weights[count++] = phase == 1 ? 2 : 3;
            }
            if (phase >= 2 && budget >= 2 && availableSlots >= 2)
            {
                candidates[count] = HordeSpawnRuleKind.PincerPressure;
                weights[count++] = phase >= 3 ? 4 : 2;
            }
            if (phase >= 3 && budget >= 3 && availableSlots >= 1 &&
                !HasGunship())
            {
                candidates[count] = HordeSpawnRuleKind.GunshipCommand;
                weights[count++] = 5;
            }

            int totalWeight = 0;
            for (int index = 0; index < count; index++)
            {
                if (candidates[index] == lastRule && count > 1)
                    weights[index] = Mathf.Max(1, weights[index] / 2);
                if (killsSinceDecision >= 3 &&
                    (candidates[index] == HordeSpawnRuleKind.PincerPressure ||
                     candidates[index] == HordeSpawnRuleKind.GunshipCommand))
                {
                    weights[index] += 2;
                }
                totalWeight += weights[index];
            }

            int roll = random.Next(0, Mathf.Max(1, totalWeight));
            for (int index = 0; index < count; index++)
            {
                if (roll < weights[index])
                    return candidates[index];
                roll -= weights[index];
            }
            return HordeSpawnRuleKind.InterceptorScreen;
        }

        int BuildRuleRoles(
            HordeSpawnRuleKind rule,
            int phase,
            int budget,
            int availableSlots,
            HordeEnemyRole[] roles)
        {
            int count = 0;
            switch (rule)
            {
                case HordeSpawnRuleKind.StrikerEscort:
                    count = AppendRole(
                        roles,
                        count,
                        HordeEnemyRole.Striker,
                        ref budget,
                        ref availableSlots);
                    count = AppendRole(
                        roles,
                        count,
                        HordeEnemyRole.Interceptor,
                        ref budget,
                        ref availableSlots);
                    break;
                case HordeSpawnRuleKind.PincerPressure:
                    count = AppendRole(
                        roles,
                        count,
                        HordeEnemyRole.Interceptor,
                        ref budget,
                        ref availableSlots);
                    count = AppendRole(
                        roles,
                        count,
                        HordeEnemyRole.Interceptor,
                        ref budget,
                        ref availableSlots);
                    if (phase >= 3 && CountRole(HordeEnemyRole.Striker) < 2)
                    {
                        count = AppendRole(
                            roles,
                            count,
                            HordeEnemyRole.Striker,
                            ref budget,
                            ref availableSlots);
                    }
                    count = AppendRole(
                        roles,
                        count,
                        HordeEnemyRole.Interceptor,
                        ref budget,
                        ref availableSlots);
                    break;
                case HordeSpawnRuleKind.GunshipCommand:
                    count = AppendRole(
                        roles,
                        count,
                        HordeEnemyRole.Gunship,
                        ref budget,
                        ref availableSlots);
                    count = AppendRole(
                        roles,
                        count,
                        HordeEnemyRole.Interceptor,
                        ref budget,
                        ref availableSlots);
                    break;
                default:
                    int desired = rule == HordeSpawnRuleKind.OpeningSweep
                        ? 2
                        : phase >= 2 ? 3 : 2;
                    while (count < desired && budget > 0 && availableSlots > 0)
                    {
                        count = AppendRole(
                            roles,
                            count,
                            HordeEnemyRole.Interceptor,
                            ref budget,
                            ref availableSlots);
                    }
                    break;
            }
            return count;
        }

        static int AppendRole(
            HordeEnemyRole[] roles,
            int count,
            HordeEnemyRole role,
            ref int budget,
            ref int availableSlots)
        {
            int cost = ThreatCost(role);
            if (roles == null || count >= roles.Length ||
                availableSlots <= 0 || budget < cost)
                return count;
            roles[count] = role;
            budget -= cost;
            availableSlots--;
            return count + 1;
        }

        bool TrySelectSector(
            HordeSpawnRuleKind rule,
            int memberCount,
            out int sector)
        {
            sector = 0;
            int[] options = rule == HordeSpawnRuleKind.PincerPressure
                ? FlankSectors
                : rule == HordeSpawnRuleKind.OpeningSweep
                    ? FrontSectors
                    : WideSectors;
            int maximumInSector = Mathf.Max(
                2,
                Mathf.CeilToInt(CurrentActiveCap * 0.4f));
            int start = random.Next(0, options.Length);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int offset = 0; offset < options.Length; offset++)
                {
                    int candidate = options[(start + offset) % options.Length];
                    if (pass == 0 && options.Length > 1 && candidate == lastSector)
                        continue;
                    int sectorIndex = candidate + 3;
                    if (sectorIndex < 0 || sectorIndex >= sectorReadyAt.Length ||
                        Time.time < sectorReadyAt[sectorIndex] ||
                        CountAssignedToSector(candidate) + memberCount >
                        maximumInSector ||
                        WouldExceedPressureDirectionCap(candidate))
                    {
                        continue;
                    }
                    sector = candidate;
                    return true;
                }
            }
            return false;
        }

        bool WouldExceedPressureDirectionCap(int candidateSector)
        {
            if (edpcg == null || edpcg.Settings.pressureDirectionCap >= 3)
                return false;
            bool left = false;
            bool center = false;
            bool right = false;
            foreach (QueuedSpawn queued in spawnQueue)
                MarkDirection(queued.Sector, ref left, ref center, ref right);
            if (playerBody != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(
                    playerBody.transform.forward,
                    Vector3.up);
                if (forward.sqrMagnitude < 0.01f)
                    forward = Vector3.forward;
                for (int index = 0; index < active.Count; index++)
                {
                    HordeEnemyVehicle enemy = active[index];
                    if (enemy == null || !enemy.IsCombatCapable)
                        continue;
                    Vector3 direction = Vector3.ProjectOnPlane(
                        enemy.BodyPosition - playerBody.worldCenterOfMass,
                        Vector3.up);
                    if (direction.sqrMagnitude < 0.01f)
                        continue;
                    int sector = Mathf.RoundToInt(Vector3.SignedAngle(
                        forward,
                        direction,
                        Vector3.up) / 45f);
                    MarkDirection(sector, ref left, ref center, ref right);
                }
            }
            int occupied = (left ? 1 : 0) + (center ? 1 : 0) +
                           (right ? 1 : 0);
            int bucket = DirectionBucket(candidateSector);
            bool alreadyOccupied = bucket < 0
                ? left
                : bucket > 0 ? right : center;
            return !alreadyOccupied &&
                   occupied >= edpcg.Settings.pressureDirectionCap;
        }

        static void MarkDirection(
            int sector,
            ref bool left,
            ref bool center,
            ref bool right)
        {
            int bucket = DirectionBucket(sector);
            if (bucket < 0)
                left = true;
            else if (bucket > 0)
                right = true;
            else
                center = true;
        }

        static int DirectionBucket(int sector)
        {
            return sector < -1 ? -1 : sector > 1 ? 1 : 0;
        }

        int CountAssignedToSector(int sector)
        {
            int count = 0;
            foreach (QueuedSpawn queued in spawnQueue)
                if (queued.Sector == sector)
                    count++;
            if (playerBody == null)
                return count;

            Vector3 forward = Vector3.ProjectOnPlane(
                playerBody.transform.forward,
                Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            forward.Normalize();
            float targetAngle = sector * 45f;
            for (int index = 0; index < active.Count; index++)
            {
                HordeEnemyVehicle enemy = active[index];
                if (enemy == null || !enemy.IsCombatCapable)
                    continue;
                Vector3 direction = Vector3.ProjectOnPlane(
                    enemy.BodyPosition - playerBody.worldCenterOfMass,
                    Vector3.up);
                if (direction.sqrMagnitude < 0.01f)
                    continue;
                float angle = Vector3.SignedAngle(forward, direction, Vector3.up);
                if (Mathf.Abs(Mathf.DeltaAngle(targetAngle, angle)) <= 30f)
                    count++;
            }
            return count;
        }

        HordeFormationKind SelectFormation(
            HordeSpawnRuleKind rule,
            int phase)
        {
            if (rule == HordeSpawnRuleKind.PincerPressure)
                return HordeFormationKind.Pincer;
            if (rule == HordeSpawnRuleKind.StrikerEscort)
                return HordeFormationKind.Echelon;

            HordeFormationKind[] options =
                rule == HordeSpawnRuleKind.GunshipCommand
                    ? new[] { HordeFormationKind.V, HordeFormationKind.LineAbreast }
                    : phase == 1
                        ? new[] { HordeFormationKind.V, HordeFormationKind.LineAbreast }
                        : new[]
                        {
                            HordeFormationKind.V,
                            HordeFormationKind.LineAbreast,
                            HordeFormationKind.Echelon
                        };
            int start = random.Next(0, options.Length);
            for (int offset = 0; offset < options.Length; offset++)
            {
                HordeFormationKind candidate =
                    options[(start + offset) % options.Length];
                if (candidate != lastFormation || options.Length == 1)
                    return candidate;
            }
            return options[start];
        }

        static float WarningSecondsFor(HordeSpawnRuleKind rule)
        {
            switch (rule)
            {
                case HordeSpawnRuleKind.GunshipCommand:
                    return 2.5f;
                case HordeSpawnRuleKind.PincerPressure:
                    return 1.8f;
                case HordeSpawnRuleKind.StrikerEscort:
                    return 1.6f;
                case HordeSpawnRuleKind.OpeningSweep:
                    return 0.9f;
                default:
                    return 1.2f;
            }
        }

        static int ThreatCost(HordeEnemyRole role)
        {
            return role == HordeEnemyRole.Gunship ? 3 :
                role == HordeEnemyRole.Striker ? 2 : 1;
        }

        int ActiveThreat()
        {
            int threat = 0;
            for (int index = 0; index < active.Count; index++)
            {
                HordeEnemyVehicle enemy = active[index];
                if (enemy != null && enemy.IsCombatCapable)
                    threat += ThreatCost(enemy.Role);
            }
            return threat;
        }

        int QueuedThreat()
        {
            int threat = 0;
            foreach (QueuedSpawn queued in spawnQueue)
                threat += ThreatCost(queued.Role);
            return threat;
        }

        int CountRole(HordeEnemyRole role)
        {
            int count = 0;
            for (int index = 0; index < active.Count; index++)
            {
                HordeEnemyVehicle enemy = active[index];
                if (enemy != null && enemy.IsCombatCapable && enemy.Role == role)
                    count++;
            }
            foreach (QueuedSpawn queued in spawnQueue)
                if (queued.Role == role)
                    count++;
            return count;
        }

        bool TrySpawnQueued()
        {
            QueuedSpawn queued = spawnQueue.Dequeue();
            if (Time.time < queued.ReadyAt)
            {
                spawnQueue.Enqueue(queued);
                return false;
            }
            if (!TryResolveSpawnPosition(queued, out Vector3 position))
            {
                queued.TotalAttempts++;
                queued.Attempts = (queued.Attempts + 1) %
                                  MaximumSpawnAttempts;
                if (edpcg != null)
                {
                    queued.ReadyAt = Time.time + Mathf.Min(
                        6f,
                        0.5f + queued.TotalAttempts * 0.35f);
                    edpcg.MarkSpawnRecovery(
                        queued.RosterMemberId,
                        queued.TotalAttempts);
                    spawnQueue.Enqueue(queued);
                }
                else if (queued.TotalAttempts < MaximumSpawnAttempts)
                {
                    spawnQueue.Enqueue(queued);
                }
                return false;
            }
            HordeEnemyVehicle enemy = FindAvailableEnemy();
            if (enemy == null)
            {
                spawnQueue.Enqueue(queued);
                return false;
            }
            Vector3 direction = playerBody != null
                ? playerBody.worldCenterOfMass - position
                : battleCenter - position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
                direction = Vector3.forward;
            Quaternion rotation = Quaternion.LookRotation(
                direction.normalized,
                Vector3.up);
            enemy.Activate(
                HordeEnemyProfile.ForRole(queued.Role),
                playerBody,
                position,
                rotation,
                spawnSequence * 4 + queued.MemberIndex,
                sessionId,
                queued.RosterMemberId,
                queued.RosterIndex,
                queued.EnvironmentalPursuer);
            active.Add(enemy);
            edpcg?.MarkSpawned(queued.RosterMemberId, position);
            return true;
        }

        bool TryResolveSpawnPosition(
            QueuedSpawn queued,
            out Vector3 position)
        {
            position = Vector3.zero;
            if (playerBody == null || navigation == null)
                return false;
            if (plannedIngresses.Count > 0)
                return TryResolvePlannedIngressPosition(queued, out position);
            Vector3 forward = Vector3.ProjectOnPlane(
                playerBody.transform.forward,
                Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            forward.Normalize();
            float attemptOffset = queued.Attempts == 0
                ? 0f
                : (queued.Attempts % 2 == 1 ? 1f : -1f) *
                  Mathf.Ceil(queued.Attempts * 0.5f) * 7.5f;
            float angle = queued.Sector * 45f + attemptOffset;
            Vector3 radial = Quaternion.AngleAxis(angle, Vector3.up) * forward;
            float speedDistance = Mathf.Clamp(
                playerBody.velocity.magnitude * 4f,
                190f,
                275f);
            float distance = speedDistance +
                             (queued.Attempts * 17 + spawnSequence * 11) % 36;
            Vector3 lateral = Vector3.Cross(Vector3.up, radial).normalized;
            float centered = queued.MemberIndex - (queued.MemberCount - 1) * 0.5f;
            Vector3 offset;
            switch (queued.Formation)
            {
                case HordeFormationKind.LineAbreast:
                    offset = lateral * centered * 38f;
                    break;
                case HordeFormationKind.Pincer:
                    offset = lateral * Mathf.Sign(centered == 0f ? 1f : centered) *
                             (30f + Mathf.Abs(centered) * 20f) -
                             radial * Mathf.Abs(centered) * 14f;
                    break;
                case HordeFormationKind.Echelon:
                    offset = lateral * centered * 34f +
                             Vector3.up * centered * 15f -
                             radial * Mathf.Abs(centered) * 12f;
                    break;
                default:
                    offset = lateral * centered * 38f -
                             radial * Mathf.Abs(centered) * 22f;
                    break;
            }
            Vector3 candidate = playerBody.worldCenterOfMass +
                                radial * distance + offset;
            if (!navigation.TrySampleGround(candidate.x, candidate.z, out float ground))
                return false;
            float roleHeight = queued.Role == HordeEnemyRole.Gunship ? 105f : 70f;
            candidate.y = Mathf.Max(candidate.y + 20f, ground + roleHeight);
            float playerDistance = Vector3.Distance(
                candidate,
                playerBody.worldCenterOfMass);
            if (playerDistance < 170f || playerDistance > 320f ||
                Vector3.Distance(candidate, battleCenter) > warningRadius * 0.75f)
                return false;
            if (playerDistance < 250f && IsUnoccludedInPlayerView(candidate))
                return false;
            for (int index = 0; index < active.Count; index++)
            {
                HordeEnemyVehicle other = active[index];
                if (other != null && other.IsCombatCapable &&
                    Vector3.Distance(candidate, other.BodyPosition) < 35f)
                    return false;
            }
            int overlapCount = Physics.OverlapSphereNonAlloc(
                candidate,
                12f,
                spawnOverlaps,
                ~0,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < overlapCount; index++)
            {
                Collider collider = spawnOverlaps[index];
                if (collider != null && collider.enabled)
                    return false;
            }
            Vector3 inbound = playerBody.worldCenterOfMass + Vector3.up * 25f;
            if (!navigation.HasStaticClearCorridor(candidate, inbound))
                return false;
            position = candidate;
            return true;
        }

        bool TryResolvePlannedIngressPosition(
            QueuedSpawn queued,
            out Vector3 position)
        {
            position = Vector3.zero;
            int ingressIndex = SelectPlannedIngressIndex(
                plannedIngresses,
                playerBody.worldCenterOfMass,
                playerBody.transform.forward,
                queued.Sector,
                queued.Attempts);
            if (ingressIndex < 0)
                return false;

            Vector3 entrance = plannedIngresses[ingressIndex];
            Vector3 radial = Vector3.ProjectOnPlane(
                battleCenter - entrance,
                Vector3.up);
            if (radial.sqrMagnitude < 0.01f)
                radial = Vector3.ProjectOnPlane(
                    playerBody.worldCenterOfMass - entrance,
                    Vector3.up);
            if (radial.sqrMagnitude < 0.01f)
                radial = Vector3.forward;
            radial.Normalize();
            Vector3 lateral = Vector3.Cross(Vector3.up, radial).normalized;
            float centered = queued.MemberIndex -
                             (queued.MemberCount - 1) * 0.5f;
            Vector3 offset;
            switch (queued.Formation)
            {
                case HordeFormationKind.LineAbreast:
                    offset = lateral * centered * 38f;
                    break;
                case HordeFormationKind.Pincer:
                    offset = lateral * Mathf.Sign(centered == 0f ? 1f : centered) *
                             (30f + Mathf.Abs(centered) * 20f) -
                             radial * Mathf.Abs(centered) * 14f;
                    break;
                case HordeFormationKind.Echelon:
                    offset = lateral * centered * 34f +
                             Vector3.up * centered * 15f -
                             radial * Mathf.Abs(centered) * 12f;
                    break;
                default:
                    offset = lateral * centered * 38f -
                             radial * Mathf.Abs(centered) * 22f;
                    break;
            }

            Vector3 candidate = entrance + offset;
            if (!navigation.TrySampleGround(
                    candidate.x,
                    candidate.z,
                    out float ground))
            {
                return false;
            }
            float roleHeight = queued.Role == HordeEnemyRole.Gunship
                ? 105f
                : 70f;
            candidate.y = ground + roleHeight +
                          Mathf.Max(0f, offset.y);

            float playerDistance = Vector3.Distance(
                candidate,
                playerBody.worldCenterOfMass);
            float centerDistance = Vector3.Distance(candidate, battleCenter);
            if (playerDistance < 170f ||
                playerDistance > warningRadius * 0.95f ||
                centerDistance > warningRadius * 0.82f)
            {
                return false;
            }
            // First try every PCG entrance without a visible pop. If the whole
            // arena is visible, the existing arrival warning is preferable to
            // silently starving the wave after all entrances were checked.
            if (queued.Attempts < plannedIngresses.Count &&
                IsUnoccludedInPlayerView(candidate))
            {
                return false;
            }
            for (int index = 0; index < active.Count; index++)
            {
                HordeEnemyVehicle other = active[index];
                if (other != null && other.IsCombatCapable &&
                    Vector3.Distance(candidate, other.BodyPosition) < 35f)
                {
                    return false;
                }
            }
            int overlapCount = Physics.OverlapSphereNonAlloc(
                candidate,
                12f,
                spawnOverlaps,
                ~0,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < overlapCount; index++)
            {
                Collider collider = spawnOverlaps[index];
                if (collider != null && collider.enabled)
                    return false;
            }

            Vector3 inbound = playerBody.worldCenterOfMass +
                              Vector3.up * 25f;
            if (!navigation.FindPath(candidate, inbound, spawnPathProbe))
                return false;
            position = candidate;
            return true;
        }

        static bool IsUnoccludedInPlayerView(Vector3 position)
        {
            Camera camera = Camera.main;
            if (camera == null || !camera.isActiveAndEnabled)
                return false;
            Vector3 delta = position - camera.transform.position;
            float distance = delta.magnitude;
            if (distance < 0.01f)
                return true;
            Vector3 direction = delta / distance;
            if (Vector3.Dot(camera.transform.forward, direction) < 0.42f)
                return false;
            Vector3 origin = camera.transform.position + direction * 2.5f;
            return !Physics.Raycast(
                origin,
                direction,
                Mathf.Max(0f, distance - 5f),
                ~0,
                QueryTriggerInteraction.Ignore);
        }

        HordeEnemyVehicle FindAvailableEnemy()
        {
            for (int index = 0; index < pool.Count; index++)
                if (pool[index] != null && !pool[index].gameObject.activeSelf)
                    return pool[index];
            return null;
        }

        bool HasGunship()
        {
            for (int index = 0; index < active.Count; index++)
                if (active[index] != null &&
                    active[index].IsCombatCapable &&
                    active[index].Role == HordeEnemyRole.Gunship)
                    return true;
            foreach (QueuedSpawn queued in spawnQueue)
                if (queued.Role == HordeEnemyRole.Gunship)
                    return true;
            return false;
        }

        void QueueRecoveredMember(
            HordeEnemyVehicle enemy,
            float delaySeconds)
        {
            if (enemy == null || edpcg == null ||
                !edpcg.TryGetAssignment(
                    enemy.RosterMemberId,
                    out EdpcgRosterAssignment assignment))
            {
                return;
            }
            int sector = Mathf.Clamp(
                assignment.RosterIndex % SectorCount - 3,
                -3,
                3);
            spawnQueue.Enqueue(new QueuedSpawn
            {
                Role = assignment.Role,
                Formation = HordeFormationKind.Echelon,
                Sector = sector,
                MemberIndex = 0,
                MemberCount = 1,
                Attempts = 0,
                TotalAttempts = 0,
                ReadyAt = Time.time + Mathf.Max(0.25f, delaySeconds),
                RosterMemberId = assignment.RosterMemberId,
                RosterIndex = assignment.RosterIndex,
                EnvironmentalPursuer = assignment.EnvironmentalPursuer
            });
        }

        void UpdateEdpcgSnapshot()
        {
            if (edpcg == null)
                return;
            int engagements = 0;
            int suicideCommits = 0;
            int rangedFireLanes = 0;
            int navigationRecoveries = 0;
            int threat = 0;
            for (int index = 0; index < active.Count; index++)
            {
                HordeEnemyVehicle enemy = active[index];
                if (enemy == null || !enemy.IsCombatCapable)
                    continue;
                threat += Mathf.Max(1, enemy.Profile.threatCost);
                if (enemy.IsThreatening ||
                    enemy.IsEnvironmentalTrapCommitted)
                    engagements++;
                if (enemy.IsSuicideCommit)
                    suicideCommits++;
                if (enemy.IsRangedFiring)
                    rangedFireLanes++;
                if (enemy.TacticalState ==
                    EdpcgEnemyTacticalState.NavigationRecovery)
                {
                    navigationRecoveries++;
                }
            }
            edpcg.UpdateDirectorSnapshot(
                AliveCount,
                engagements,
                attackTokens,
                suicideCommits,
                rangedFireLanes,
                navigationRecoveries,
                threat);
        }

        static bool InReliefWindow(float seconds)
        {
            return seconds >= 26f && seconds < FinalClearStartSeconds &&
                   seconds % 30f >= 26f;
        }
    }

    [DisallowMultipleComponent]
    public sealed class HordeEnemyVehicle :
        MonoBehaviour,
        ISpaceDamageable
    {
        enum AttackState
        {
            Tracking,
            Telegraph,
            Firing,
            Recovering
        }

        readonly VehicleForceLedger forceLedger = new VehicleForceLedger();
        readonly List<Vector3> route = new List<Vector3>(24);
        HordeCombatDirector director;
        WeaponVisualPool visuals;
        WeaponProjectilePool projectiles;
        Rigidbody playerBody;
        Rigidbody body;
        BoxCollider rootCollider;
        AudioSource reengagementAudio;
        AudioLowPassFilter reengagementLowPass;
        static AudioClip reengagementClip;
        Renderer[] renderers = Array.Empty<Renderer>();
        Material hullMaterial;
        GameObject fallbackHullVisual;
        GameObject interceptorVisual;
        GameObject strikerVisual;
        GameObject gunshipVisual;
        GameObject activeHullVisual;
        MaterialPropertyBlock hullPaint;
        HordeEnemyProfile profile;
        WeaponProfile weaponProfile;
        Vector3 desiredVelocity;
        Vector3 desiredAim;
        Vector3 lastProgressPosition;
        Vector3 tacticalDestination;
        Vector3 suicideCommitAim;
        float health;
        float spawnAt;
        float nextReplanAt;
        float progressCheckedAt;
        float nextSemanticDecisionAt;
        float nextPathRetryAt;
        float stuckSince;
        float outsideSeconds;
        float attackStateUntil;
        float nextShotAt;
        float nextSuicideWarningAt;
        float suicideCommitEndsAt;
        float lineOfSightLostAt;
        float lastCombatContactAt;
        float environmentalImpactArmedUntil;
        float environmentalImpactScale;
        float environmentallyCapturedUntil;
        float returnAt;
        int routeIndex;
        int sessionId;
        int pathFailureCount;
        int staticCollisionCount;
        bool activeForCombat;
        bool dead;
        bool hasAttackToken;
        bool reengaging;
        bool environmentalTrapCommitted;
        GameObject environmentalImpactSource;
        AttackState attackState;

        public HordeEnemyProfile Profile => profile;
        public HordeEnemyRole Role => profile?.role ?? HordeEnemyRole.Interceptor;
        public int SlotIndex { get; private set; }
        public bool HasAttackToken => hasAttackToken;
        public bool IsThreatening =>
            attackState == AttackState.Telegraph ||
            attackState == AttackState.Firing;
        public Vector3 BodyPosition => body != null ? body.position : transform.position;
        public bool IsCombatCapable => activeForCombat && !dead && health > 0f;
        public bool ReadyForPool => dead && Time.time >= returnAt;
        public float Integrity => health;
        public float MaximumIntegrity => profile?.maximumHealth ?? 0f;
        public bool IsDestroyed => dead;
        public bool IsSuicide =>
            profile != null &&
            profile.attackKind == HordeEnemyAttackKind.Suicide;
        public bool IsSuicideCommit => IsSuicide &&
                                       attackState == AttackState.Firing;
        public bool IsRangedFiring => !IsSuicide &&
                                      attackState == AttackState.Firing;
        public bool IsReengaging => reengaging;
        public bool IsEnvironmentalTrapCommitted =>
            environmentalTrapCommitted;
        public bool IsEnvironmentalPursuer { get; private set; }
        public Rigidbody Body => body;
        public bool CanAcceptEnvironmentalTrapCommit =>
            IsCombatCapable && Role != HordeEnemyRole.Gunship &&
            attackState != AttackState.Telegraph &&
            attackState != AttackState.Firing &&
            attackState != AttackState.Recovering &&
            Time.time >= spawnAt + 1.25f;
        public string RosterMemberId { get; private set; } = string.Empty;
        public int RosterIndex { get; private set; } = -1;
        public EdpcgPathIntent PathIntent { get; private set; } =
            EdpcgPathIntent.Ingress;
        public EdpcgEnemyTacticalState TacticalState { get; private set; } =
            EdpcgEnemyTacticalState.Ingress;
        public EdpcgWeaponPermission WeaponPermission { get; private set; } =
            EdpcgWeaponPermission.Safe;

        public bool Initialize(
            HordeCombatDirector source,
            WeaponVisualPool effectPool,
            WeaponProjectilePool projectilePool,
            out string error)
        {
            error = string.Empty;
            director = source;
            visuals = effectPool;
            projectiles = projectilePool;
            hullPaint = new MaterialPropertyBlock();
            body = gameObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.drag = 0f;
            body.angularDrag = 0f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.solverIterations = 8;
            body.solverVelocityIterations = 3;
            body.maxAngularVelocity = 8f;
            rootCollider = gameObject.AddComponent<BoxCollider>();
            rootCollider.size = new Vector3(7f, 2.5f, 10f);
            rootCollider.center = new Vector3(0f, 0f, -0.5f);
            VehicleCombatTeamUtility.SetTeam(gameObject, VehicleCombatTeam.Enemy);
            ConfigureReengagementAudio();

            Shader shader = Shader.Find("Standard");
            if (shader == null || !shader.isSupported ||
                shader.name.IndexOf(
                    "InternalErrorShader",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                error = "割草敌机需要受支持的 Built-in Standard Shader。";
                return false;
            }
            hullMaterial = new Material(shader)
            {
                name = "RuntimeHordeEnemyHull"
            };
            BuildHull();
            BuildFleetVisuals();
            SelectRoleVisual(HordeEnemyRole.Interceptor);
            if (!Forge3DEffectPool.HasRenderableRendererSet(gameObject))
            {
                error = "割草敌机机身存在缺失或不受支持的材质。";
                return false;
            }
            return true;
        }

        public void Activate(
            HordeEnemyProfile selectedProfile,
            Rigidbody targetBody,
            Vector3 position,
            Quaternion rotation,
            int slotIndex,
            int newSessionId,
            string rosterMemberId = "",
            int rosterIndex = -1,
            bool environmentalPursuer = false)
        {
            profile = selectedProfile ?? HordeEnemyProfile.ForRole(
                HordeEnemyRole.Interceptor);
            playerBody = targetBody;
            SlotIndex = slotIndex;
            sessionId = newSessionId;
            RosterMemberId = rosterMemberId ?? string.Empty;
            RosterIndex = rosterIndex;
            IsEnvironmentalPursuer = environmentalPursuer;
            health = profile.maximumHealth;
            activeForCombat = true;
            dead = false;
            hasAttackToken = false;
            desiredVelocity = rotation * Vector3.forward * profile.maximumSpeed;
            desiredAim = rotation * Vector3.forward;
            spawnAt = Time.time;
            nextReplanAt = Time.time + (slotIndex % 5) * 0.1f;
            progressCheckedAt = Time.time;
            lastProgressPosition = position;
            tacticalDestination = position;
            nextSemanticDecisionAt = Time.time;
            nextPathRetryAt = Time.time;
            stuckSince = -1f;
            pathFailureCount = 0;
            staticCollisionCount = 0;
            outsideSeconds = 0f;
            attackState = AttackState.Tracking;
            attackStateUntil = spawnAt + 2f;
            nextShotAt = 0f;
            nextSuicideWarningAt = 0f;
            suicideCommitEndsAt = 0f;
            lineOfSightLostAt = -1f;
            lastCombatContactAt = spawnAt;
            environmentalImpactArmedUntil = -1f;
            environmentalImpactScale = 0f;
            environmentalImpactSource = null;
            environmentallyCapturedUntil = -1f;
            returnAt = float.PositiveInfinity;
            reengaging = false;
            environmentalTrapCommitted = false;
            PathIntent = EdpcgPathIntent.Ingress;
            TacticalState = EdpcgEnemyTacticalState.Ingress;
            WeaponPermission = EdpcgWeaponPermission.Safe;
            route.Clear();
            routeIndex = 0;
            ConfigureForProfile();
            ResetReengagementAudio();

            body.isKinematic = true;
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            gameObject.SetActive(true);
            rootCollider.enabled = true;
            SetRenderers(true);
            body.isKinematic = false;
            body.velocity = rotation * Vector3.forward *
                            Mathf.Min(35f, profile.maximumSpeed * 0.65f);
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
        }

        public void DeactivateForPool()
        {
            director?.ReleaseAttackToken(this);
            activeForCombat = false;
            dead = false;
            hasAttackToken = false;
            reengaging = false;
            environmentalTrapCommitted = false;
            environmentalImpactArmedUntil = -1f;
            environmentalImpactScale = 0f;
            environmentalImpactSource = null;
            environmentallyCapturedUntil = -1f;
            StopReengagementAudio();
            RosterMemberId = string.Empty;
            RosterIndex = -1;
            IsEnvironmentalPursuer = false;
            PathIntent = EdpcgPathIntent.Ingress;
            TacticalState = EdpcgEnemyTacticalState.Resolved;
            WeaponPermission = EdpcgWeaponPermission.Safe;
            route.Clear();
            routeIndex = 0;
            if (rootCollider != null)
                rootCollider.enabled = false;
            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.isKinematic = true;
            }
            SetRenderers(false);
            gameObject.SetActive(false);
        }

        public void SetAttackToken(bool value)
        {
            hasAttackToken = value;
        }

        public void ApplyDamage(SpaceDamageInfo damage)
        {
            if (!IsCombatCapable || damage.amount <= 0f)
                return;
            float applied = Mathf.Min(health, damage.amount);
            health = Mathf.Max(0f, health - damage.amount);
            director?.NotifyEnemyDamaged(applied);
            if (body != null && !body.isKinematic &&
                damage.impulse.sqrMagnitude > 0.0001f)
            {
                body.AddForceAtPosition(
                    damage.impulse,
                    damage.point,
                    ForceMode.Impulse);
            }
            if (health <= 0f)
                Die(
                    damage,
                    applied,
                    EdpcgEnemyResolutionReason.KilledByPlayer);
        }

        public void ArmEnvironmentalTrapImpact(
            float intensity,
            GameObject source)
        {
            if (!IsCombatCapable)
                return;
            environmentalImpactArmedUntil = Time.time + 0.38f;
            environmentalImpactScale = Mathf.Clamp(intensity, 0.35f, 1.35f);
            environmentalImpactSource = source;
        }

        public void NotifyEnvironmentalCapture(float holdSeconds)
        {
            if (!IsCombatCapable)
                return;
            environmentallyCapturedUntil = Mathf.Max(
                environmentallyCapturedUntil,
                Time.time + Mathf.Max(0.05f, holdSeconds));
        }

        void Update()
        {
            if (!activeForCombat || dead || playerBody == null ||
                director == null || director.SessionId != sessionId)
                return;
            if (PlayerSkillCombatEffects.AreEnemiesFrozen)
            {
                UpdateReengagementAudio(false);
                return;
            }
            UpdateNavigation();
            UpdateReengagementAudio(
                reengaging || environmentalTrapCommitted);
            UpdateAttack();
            UpdateEscapeState();
        }

        void FixedUpdate()
        {
            if (!IsCombatCapable || body == null || body.isKinematic)
                return;
            PlanetEnvironmentSample environment = PlanetEnvironmentRuntime.Sample(
                body.worldCenterOfMass,
                Time.fixedTimeAsDouble);
            Vector3 gravity = environment.gravityAcceleration;
            Vector3 up = gravity.sqrMagnitude > 0.001f
                ? -gravity.normalized
                : Vector3.up;
            Vector3 relativeVelocity = body.velocity -
                                       environment.atmosphereVelocity;
            forceLedger.Begin(body);
            forceLedger.AddForce(body.mass * gravity);

            float desiredForwardSpeed = Mathf.Clamp(
                Vector3.Dot(desiredVelocity, transform.forward),
                0f,
                profile.maximumSpeed);
            float currentForwardSpeed = Vector3.Dot(
                relativeVelocity,
                transform.forward);
            float forwardAcceleration = Mathf.Clamp(
                (desiredForwardSpeed - currentForwardSpeed) * 1.1f,
                0f,
                profile.maximumAcceleration);
            forceLedger.AddForce(
                transform.forward * body.mass * forwardAcceleration);

            Vector3 lateralError = Vector3.ProjectOnPlane(
                desiredVelocity - relativeVelocity,
                transform.forward);
            Vector3 controlAcceleration = Vector3.ClampMagnitude(
                lateralError * 0.65f,
                profile.maximumAcceleration * 0.45f);
            forceLedger.AddForce(body.mass * controlAcceleration);

            float gravityMagnitude = gravity.magnitude;
            float desiredVertical = Vector3.Dot(desiredVelocity, up);
            float currentVertical = Vector3.Dot(relativeVelocity, up);
            float liftAcceleration = gravityMagnitude + Mathf.Clamp(
                (desiredVertical - currentVertical) * 1.8f,
                -gravityMagnitude * 0.55f,
                gravityMagnitude * 0.7f);
            forceLedger.AddForce(up * body.mass * liftAcceleration);

            if (environment.hasAtmosphere && environment.airDensity > 0.0001f)
            {
                float speed = relativeVelocity.magnitude;
                if (speed > 0.1f)
                {
                    float dragArea = profile.role == HordeEnemyRole.Gunship
                        ? 5.5f
                        : profile.role == HordeEnemyRole.Striker ? 4.2f : 3.2f;
                    Vector3 drag = -relativeVelocity.normalized *
                                   (0.5f * environment.airDensity *
                                    speed * speed * 0.32f * dragArea);
                    forceLedger.AddForce(drag);
                }
            }

            Vector3 desiredForward = desiredAim.sqrMagnitude > 0.001f
                ? desiredAim.normalized
                : desiredVelocity.sqrMagnitude > 0.001f
                    ? desiredVelocity.normalized
                    : transform.forward;
            if (Mathf.Abs(Vector3.Dot(desiredForward, up)) > 0.94f)
                desiredForward = Vector3.ProjectOnPlane(desiredForward, up).normalized;
            Vector3 orientationError =
                Vector3.Cross(transform.forward, desiredForward) * 3.2f +
                Vector3.Cross(transform.up, up) * 1.7f;
            Vector3 angularAcceleration = Vector3.ClampMagnitude(
                orientationError * 2.4f - body.angularVelocity * 2.1f,
                profile.role == HordeEnemyRole.Interceptor ? 4.2f : 3.2f);
            forceLedger.AddTorque(TorqueForAngularAcceleration(angularAcceleration));
            forceLedger.Apply();
        }

        void UpdateNavigation()
        {
            if (Time.time >= nextSemanticDecisionAt)
            {
                UpdateReengagementState();
                EdpcgPathIntent nextIntent = ChoosePathIntent();
                Vector3 nextDestination = director.ResolveTacticalDestination(
                    this,
                    nextIntent);
                bool semanticChanged = nextIntent != PathIntent ||
                                       Vector3.Distance(
                                           nextDestination,
                                           tacticalDestination) > 35f;
                PathIntent = nextIntent;
                tacticalDestination = nextDestination;
                nextSemanticDecisionAt = Time.time +
                                         director.SemanticReplanSeconds;
                if (semanticChanged)
                    nextReplanAt = Time.time;
            }

            if (Time.time - progressCheckedAt >= 1f)
            {
                float moved = Vector3.Distance(
                    body.position,
                    lastProgressPosition);
                if (moved >= 5f)
                {
                    stuckSince = -1f;
                    staticCollisionCount = Mathf.Max(0, staticCollisionCount - 1);
                }
                else if (stuckSince < 0f)
                {
                    stuckSince = Time.time;
                }
                progressCheckedAt = Time.time;
                lastProgressPosition = body.position;
            }

            float stuckSeconds = stuckSince < 0f
                ? 0f
                : Time.time - stuckSince;
            if (director.EdpcgRuntime != null &&
                stuckSeconds >= director.NavigationRecoveryAfterSeconds)
            {
                BeginNavigationRecovery("stuck-timeout");
                return;
            }
            if (stuckSeconds >= director.EvasiveAfterSeconds)
            {
                TacticalState = EdpcgEnemyTacticalState.NavigationRecovery;
                PathIntent = EdpcgPathIntent.NavigationRecovery;
                route.Clear();
                routeIndex = 0;
                Vector3 lateral = Vector3.Cross(
                    Vector3.up,
                    playerBody.worldCenterOfMass - body.position).normalized;
                if ((SlotIndex & 1) != 0)
                    lateral = -lateral;
                desiredVelocity = Vector3.ClampMagnitude(
                    lateral * profile.maximumSpeed * 0.72f +
                    Vector3.up * profile.maximumSpeed * 0.35f,
                    profile.maximumSpeed);
                desiredAim = desiredVelocity.normalized;
                nextReplanAt = Mathf.Max(nextReplanAt, Time.time + 0.5f);
                return;
            }

            bool localRepair = stuckSeconds >= director.LocalRepairAfterSeconds;
            if ((Time.time >= nextReplanAt || localRepair || route.Count == 0) &&
                Time.time >= nextPathRetryAt)
            {
                bool found = director.TryFindPath(
                    this,
                    body.position,
                    tacticalDestination,
                    route);
                routeIndex = 0;
                nextReplanAt = Time.time + director.SemanticReplanSeconds;
                if (found && route.Count > 0)
                {
                    pathFailureCount = 0;
                    nextPathRetryAt = Time.time;
                }
                else
                {
                    route.Clear();
                    pathFailureCount++;
                    if (environmentalTrapCommitted &&
                        pathFailureCount >= 2)
                    {
                        EndEnvironmentalTrapCommit("route-not-found");
                    }
                    float retry = Mathf.Min(
                        4f,
                        0.5f * Mathf.Pow(2f, pathFailureCount - 1));
                    nextPathRetryAt = Time.time + retry;
                    director.EdpcgRuntime?.RecordPathFailure(
                        this,
                        PathIntent,
                        localRepair ? "local-repair-failed" : "route-not-found");
                }
            }
            while (routeIndex < route.Count &&
                   Vector3.Distance(body.position, route[routeIndex]) < 14f)
                routeIndex++;
            Vector3 separation = director.SampleSeparation(this) * 20f;
            if (routeIndex < route.Count)
            {
                Vector3 direction = route[routeIndex] - body.position;
                if (direction.sqrMagnitude < 0.01f)
                    direction = transform.forward;
                desiredVelocity = Vector3.ClampMagnitude(
                    direction.normalized * profile.maximumSpeed + separation,
                    profile.maximumSpeed);
            }
            else
            {
                // A failed graph query is not permission to fly straight
                // through the city. Loiter and separate until the backoff ends.
                Vector3 fromPlayer = body.position -
                                     playerBody.worldCenterOfMass;
                Vector3 tangent = Vector3.Cross(Vector3.up, fromPlayer);
                if (tangent.sqrMagnitude < 0.01f)
                    tangent = transform.right;
                if ((SlotIndex & 1) != 0)
                    tangent = -tangent;
                desiredVelocity = Vector3.ClampMagnitude(
                    tangent.normalized * profile.maximumSpeed * 0.35f +
                    separation,
                    profile.maximumSpeed * 0.45f);
            }
            Vector3 predicted = playerBody.worldCenterOfMass +
                                playerBody.velocity * Mathf.Clamp(
                                    Vector3.Distance(
                                        body.position,
                                        playerBody.worldCenterOfMass) / 240f,
                                    0f,
                                    0.8f);
            desiredAim = (predicted - body.worldCenterOfMass).normalized;
        }

        EdpcgPathIntent ChoosePathIntent()
        {
            if (Time.time < spawnAt + 1.25f)
            {
                TacticalState = EdpcgEnemyTacticalState.Ingress;
                return EdpcgPathIntent.Ingress;
            }
            if (environmentalTrapCommitted)
            {
                TacticalState =
                    EdpcgEnemyTacticalState.EnvironmentalTrapCommit;
                return EdpcgPathIntent.EnvironmentalTrapCommit;
            }
            if (reengaging)
            {
                TacticalState = EdpcgEnemyTacticalState.Reengaging;
                return EdpcgPathIntent.ReacquirePlayer;
            }
            if (IsSuicide)
            {
                if (attackState == AttackState.Firing)
                {
                    TacticalState = EdpcgEnemyTacticalState.Attacking;
                    return EdpcgPathIntent.CommitAttackRun;
                }
                if (attackState == AttackState.Telegraph)
                {
                    TacticalState = EdpcgEnemyTacticalState.Telegraphing;
                    return EdpcgPathIntent.Intercept;
                }
                if (attackState == AttackState.Recovering)
                {
                    TacticalState = EdpcgEnemyTacticalState.Disengaging;
                    return EdpcgPathIntent.BreakAway;
                }
                TacticalState = director.StrategyLevel >= 2
                    ? EdpcgEnemyTacticalState.Positioning
                    : EdpcgEnemyTacticalState.Threatening;
                return director.StrategyLevel >= 2
                    ? EdpcgPathIntent.MaskedFlank
                    : EdpcgPathIntent.Probe;
            }
            if (attackState == AttackState.Firing)
            {
                TacticalState = EdpcgEnemyTacticalState.Attacking;
                return EdpcgPathIntent.Suppress;
            }
            if (attackState == AttackState.Telegraph)
            {
                TacticalState = EdpcgEnemyTacticalState.Telegraphing;
                return EdpcgPathIntent.RangedPerch;
            }
            if (attackState == AttackState.Recovering)
            {
                TacticalState = EdpcgEnemyTacticalState.Disengaging;
                return EdpcgPathIntent.BreakLineOfSight;
            }
            TacticalState = EdpcgEnemyTacticalState.Positioning;
            return director.StrategyLevel >= 2
                ? EdpcgPathIntent.MaskedFlank
                : director.StrategyLevel >= 1
                    ? EdpcgPathIntent.RangedPerch
                    : EdpcgPathIntent.FormUp;
        }

        void UpdateReengagementState()
        {
            Vector3 playerPosition = playerBody.worldCenterOfMass;
            float playerDistance = Vector3.Distance(
                body.worldCenterOfMass,
                playerPosition);
            bool attackInProgress = attackState == AttackState.Telegraph ||
                                    attackState == AttackState.Firing;
            if (director.TryResolveEnvironmentalTrapCommit(
                    this,
                    out _))
            {
                BeginEnvironmentalTrapCommit();
                return;
            }
            if (environmentalTrapCommitted)
                EndEnvironmentalTrapCommit("opportunity-ended");
            bool environmentalLure =
                director.EdpcgRuntime == null &&
                UrbanEnvironmentalFieldDirector.TryResolveEnemyLure(
                    playerBody,
                    body.worldCenterOfMass,
                    SlotIndex,
                    Role,
                    out _);
            if (environmentalLure && !attackInProgress &&
                attackState != AttackState.Recovering)
            {
                if (!reengaging)
                {
                    route.Clear();
                    routeIndex = 0;
                    pathFailureCount = 0;
                    nextPathRetryAt = Time.time;
                    nextReplanAt = Time.time;
                }
                reengaging = true;
                return;
            }
            bool hasClearCorridor = director.HasLineOfTravel(
                body.worldCenterOfMass,
                playerPosition);
            if (HordeReengagementPolicy.HasReestablishedContact(
                    profile.attackKind,
                    playerDistance,
                    hasClearCorridor,
                    attackInProgress))
            {
                lastCombatContactAt = Time.time;
                reengaging = false;
                return;
            }

            bool attackRecovering = attackState == AttackState.Recovering;
            if (reengaging ||
                !HordeReengagementPolicy.ShouldBegin(
                    Time.time - spawnAt,
                    Time.time - lastCombatContactAt,
                    playerDistance,
                    attackInProgress,
                    attackRecovering))
            {
                return;
            }

            reengaging = true;
            route.Clear();
            routeIndex = 0;
            pathFailureCount = 0;
            nextPathRetryAt = Time.time;
            nextReplanAt = Time.time;
        }

        void BeginEnvironmentalTrapCommit()
        {
            if (environmentalTrapCommitted)
                return;
            director.ReleaseAttackToken(this);
            environmentalTrapCommitted = true;
            reengaging = false;
            attackState = AttackState.Tracking;
            attackStateUntil = Time.time + 0.35f;
            WeaponPermission = EdpcgWeaponPermission.Safe;
            route.Clear();
            routeIndex = 0;
            pathFailureCount = 0;
            nextPathRetryAt = Time.time;
            nextReplanAt = Time.time;
            nextSemanticDecisionAt = Time.time;
        }

        void EndEnvironmentalTrapCommit(string reason)
        {
            if (!environmentalTrapCommitted)
                return;
            director.ReleaseEnvironmentalTrapCommit(this, reason);
            environmentalTrapCommitted = false;
            route.Clear();
            routeIndex = 0;
            pathFailureCount = 0;
            nextPathRetryAt = Time.time;
            nextReplanAt = Time.time;
            nextSemanticDecisionAt = Time.time;
        }

        void UpdateAttack()
        {
            if (environmentalTrapCommitted)
            {
                director.ReleaseAttackToken(this);
                WeaponPermission = EdpcgWeaponPermission.Safe;
                attackState = AttackState.Tracking;
                attackStateUntil = Mathf.Max(
                    attackStateUntil,
                    Time.time + 0.25f);
                return;
            }
            if (Time.time <= environmentallyCapturedUntil)
            {
                director.ReleaseAttackToken(this);
                WeaponPermission = EdpcgWeaponPermission.Safe;
                attackState = AttackState.Recovering;
                attackStateUntil = Mathf.Max(
                    attackStateUntil,
                    Time.time + 0.38f);
                return;
            }
            if (IsSuicide)
            {
                UpdateSuicideAttack();
                return;
            }

            Vector3 target = playerBody.worldCenterOfMass;
            float distance = Vector3.Distance(body.worldCenterOfMass, target);
            bool hasLineOfSight = director.HasLineOfTravel(
                body.worldCenterOfMass,
                target);
            if (!hasLineOfSight)
                lineOfSightLostAt = Time.time;
            float hysteresis = director.EdpcgRuntime != null
                ? director.EdpcgRuntime.Settings.lineOfSightHysteresisSeconds
                : 0.2f;
            bool sightStable = hasLineOfSight &&
                               (lineOfSightLostAt < 0f ||
                                Time.time - lineOfSightLostAt >= hysteresis);
            bool canEngage = Time.time >= spawnAt + 2f &&
                             distance <= 420f && sightStable;
            switch (attackState)
            {
                case AttackState.Tracking:
                    WeaponPermission = EdpcgWeaponPermission.Safe;
                    if (canEngage && Time.time >= attackStateUntil &&
                        director.TryAcquireAttackToken(this))
                    {
                        attackState = AttackState.Telegraph;
                        WeaponPermission = EdpcgWeaponPermission.TelegraphOnly;
                        attackStateUntil = Time.time +
                            (director.EdpcgRuntime != null
                                ? director.EdpcgRuntime.Settings.
                                    rangedTelegraphSeconds
                                : 0.55f);
                    }
                    break;
                case AttackState.Telegraph:
                    WeaponPermission = EdpcgWeaponPermission.TelegraphOnly;
                    visuals?.SpawnTracer(
                        ResolveMuzzle(0),
                        target,
                        new Color(1f, 0.42f, 0.08f, 0.75f),
                        0.07f,
                        "HordeAttackTelegraph");
                    if (!canEngage)
                    {
                        director.ReleaseAttackToken(this);
                        attackState = AttackState.Recovering;
                        attackStateUntil = Time.time + 0.8f;
                    }
                    else if (Time.time >= attackStateUntil)
                    {
                        attackState = AttackState.Firing;
                        WeaponPermission = EdpcgWeaponPermission.Live;
                        attackStateUntil = Time.time +
                            (director.EdpcgRuntime != null
                                ? director.EdpcgRuntime.Settings.
                                    rangedBurstSeconds
                                : 0.8f);
                        nextShotAt = 0f;
                    }
                    break;
                case AttackState.Firing:
                    WeaponPermission = EdpcgWeaponPermission.Live;
                    if (!hasLineOfSight || distance > 420f)
                    {
                        director.ReleaseAttackToken(this);
                        WeaponPermission = EdpcgWeaponPermission.Safe;
                        attackState = AttackState.Recovering;
                        attackStateUntil = Time.time +
                            (director.EdpcgRuntime != null
                                ? director.EdpcgRuntime.Settings.
                                    rangedRelocationSeconds
                                : 0.9f);
                        break;
                    }
                    if (Time.time >= nextShotAt)
                    {
                        FireVolley(target);
                        nextShotAt = Time.time + 0.25f;
                    }
                    if (Time.time >= attackStateUntil)
                    {
                        director.ReleaseAttackToken(this);
                        WeaponPermission = EdpcgWeaponPermission.Safe;
                        attackState = AttackState.Recovering;
                        attackStateUntil = Time.time +
                            (director.EdpcgRuntime != null
                                ? director.EdpcgRuntime.Settings.
                                    rangedRelocationSeconds
                                : 0.9f);
                    }
                    break;
                default:
                    WeaponPermission = EdpcgWeaponPermission.Safe;
                    if (Time.time >= attackStateUntil)
                        attackState = AttackState.Tracking;
                    break;
            }
        }

        void UpdateSuicideAttack()
        {
            Vector3 target = playerBody.worldCenterOfMass;
            float distance = Vector3.Distance(body.worldCenterOfMass, target);
            bool hasApproach =
                director.HasLineOfTravel(body.worldCenterOfMass, target);
            switch (attackState)
            {
                case AttackState.Tracking:
                    WeaponPermission = EdpcgWeaponPermission.Safe;
                    if (Time.time >= spawnAt + 1.25f &&
                        distance <= 72f && hasApproach &&
                        director.TryAcquireAttackToken(this))
                    {
                        attackState = AttackState.Telegraph;
                        WeaponPermission = EdpcgWeaponPermission.TelegraphOnly;
                        attackStateUntil = Time.time +
                            (director.EdpcgRuntime != null
                                ? director.EdpcgRuntime.Settings.
                                    suicideTelegraphSeconds
                                : 0.55f);
                        nextSuicideWarningAt = 0f;
                    }
                    break;
                case AttackState.Telegraph:
                    WeaponPermission = EdpcgWeaponPermission.TelegraphOnly;
                    if (Time.time >= nextSuicideWarningAt)
                    {
                        nextSuicideWarningAt = Time.time + 0.12f;
                        visuals?.SpawnTracer(
                            body.worldCenterOfMass,
                            target,
                            new Color(1f, 0.12f, 0.02f, 0.9f),
                            0.1f,
                            "HordeSuicideTelegraph");
                    }
                    if (!hasApproach || distance > 88f)
                    {
                        director.ReleaseAttackToken(this);
                        attackState = AttackState.Recovering;
                        attackStateUntil = Time.time + 0.25f;
                    }
                    else if (Time.time >= attackStateUntil)
                    {
                        float commitSeconds = director.EdpcgRuntime != null
                            ? director.EdpcgRuntime.Settings.
                                suicideCommitSeconds
                            : 1.25f;
                        float lead = Mathf.Clamp(
                            distance / Mathf.Max(1f, profile.maximumSpeed),
                            0.15f,
                            commitSeconds);
                        suicideCommitAim = target +
                                           playerBody.velocity * lead;
                        suicideCommitEndsAt = Time.time + commitSeconds;
                        attackState = AttackState.Firing;
                        WeaponPermission = EdpcgWeaponPermission.Live;
                    }
                    break;
                case AttackState.Firing:
                {
                    WeaponPermission = EdpcgWeaponPermission.Live;
                    Vector3 commitDirection = suicideCommitAim -
                                              body.worldCenterOfMass;
                    if (commitDirection.sqrMagnitude > 0.01f)
                    {
                        desiredAim = commitDirection.normalized;
                        desiredVelocity = desiredAim * profile.maximumSpeed;
                    }
                    if (distance <= Mathf.Max(
                            10f,
                            profile.detonationRadius * 1.1f))
                    {
                        Detonate();
                        break;
                    }
                    bool missed = commitDirection.sqrMagnitude <= 36f ||
                                  Time.time >= suicideCommitEndsAt;
                    if (!missed)
                        break;
                    director.ReleaseAttackToken(this);
                    WeaponPermission = EdpcgWeaponPermission.Safe;
                    attackState = AttackState.Recovering;
                    attackStateUntil = Time.time +
                        (director.EdpcgRuntime != null
                            ? director.EdpcgRuntime.Settings.
                                suicideBreakAwaySeconds
                            : 2.2f);
                    PathIntent = EdpcgPathIntent.BreakAway;
                    nextSemanticDecisionAt = Time.time;
                    break;
                }
                default:
                    WeaponPermission = EdpcgWeaponPermission.Safe;
                    if (Time.time >= attackStateUntil)
                        attackState = AttackState.Tracking;
                    break;
            }
        }

        void FireVolley(Vector3 target)
        {
            if (projectiles == null || weaponProfile == null)
                return;
            for (int gun = 0; gun < profile.gunCount; gun++)
            {
                Vector3 origin = ResolveMuzzle(gun);
                Vector3 predicted = target + playerBody.velocity * Mathf.Clamp(
                    Vector3.Distance(origin, target) /
                    Mathf.Max(1f, weaponProfile.projectileSpeed),
                    0f,
                    0.8f);
                Vector3 direction = (predicted - origin).normalized;
                visuals?.SpawnMuzzle(
                    origin,
                    direction,
                    weaponProfile.effectColor,
                    weaponProfile.muzzleEffect,
                    transform);
                projectiles.Launch(
                    origin,
                    direction,
                    body.velocity,
                    weaponProfile,
                    transform,
                    null);
            }
        }

        Vector3 ResolveMuzzle(int gun)
        {
            float x = profile != null && profile.gunCount > 1
                ? (gun == 0 ? -2.1f : 2.1f)
                : 0f;
            return transform.TransformPoint(new Vector3(x, -0.1f, 5.2f));
        }

        void UpdateEscapeState()
        {
            float distance = Vector3.Distance(body.position, director == null
                ? Vector3.zero
                : director.ResolveTacticalDestination(this));
            bool outside = distance > 900f &&
                           !director.HasLineOfTravel(
                               body.position,
                               playerBody.worldCenterOfMass);
            outsideSeconds = outside
                ? outsideSeconds + Time.deltaTime
                : 0f;
            if (outsideSeconds >= 12f)
                Escape();
        }

        void Escape()
        {
            if (!IsCombatCapable)
                return;
            dead = true;
            activeForCombat = false;
            StopReengagementAudio();
            returnAt = Time.time;
            director?.NotifyEnemyEscaped(this);
            rootCollider.enabled = false;
            SetRenderers(false);
            body.isKinematic = true;
        }

        void BeginNavigationRecovery(string reason)
        {
            if (!IsCombatCapable)
                return;
            dead = true;
            activeForCombat = false;
            StopReengagementAudio();
            returnAt = Time.time;
            TacticalState = EdpcgEnemyTacticalState.NavigationRecovery;
            WeaponPermission = EdpcgWeaponPermission.Safe;
            director?.NotifyEnemyNavigationRecovery(this, reason);
            if (rootCollider != null)
                rootCollider.enabled = false;
            SetRenderers(false);
            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.isKinematic = true;
            }
        }

        void Die(
            SpaceDamageInfo damage,
            float appliedDamage,
            EdpcgEnemyResolutionReason resolutionReason)
        {
            if (dead)
                return;
            dead = true;
            activeForCombat = false;
            StopReengagementAudio();
            returnAt = Time.time + 0.35f;
            TacticalState = EdpcgEnemyTacticalState.Resolved;
            WeaponPermission = EdpcgWeaponPermission.Safe;
            director?.NotifyEnemyDeath(this, 0f, resolutionReason);
            rootCollider.enabled = false;
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            Bounds bounds = ResolveBounds();
            CombatFeedbackController.GetOrCreate().PlayDestruction(
                new ModuleDestructionFeedbackContext(
                    "$horde-enemy",
                    damage.point.sqrMagnitude > 0.001f
                        ? damage.point
                        : bounds.center,
                    damage.impulse.sqrMagnitude > 0.001f
                        ? -damage.impulse.normalized
                        : Vector3.up,
                    GridModuleCategory.Core,
                    true,
                    bounds,
                    0,
                    appliedDamage > 0.01f ? 3f : 1f));
            SetRenderers(false);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!IsCombatCapable || collision == null ||
                playerBody == null)
            {
                return;
            }
            Transform hit = collision.transform;
            if (IsSuicide &&
                (collision.rigidbody == playerBody ||
                 (hit != null && hit.IsChildOf(playerBody.transform))))
            {
                Detonate();
                return;
            }
            bool hitStaticWorld = collision.rigidbody == null ||
                                  collision.rigidbody.isKinematic;
            if (hitStaticWorld &&
                Time.time <= environmentalImpactArmedUntil)
            {
                ApplyEnvironmentalCollisionImpact(collision);
                if (!IsCombatCapable)
                    return;
            }
            if (!IsSuicide || !hitStaticWorld)
                return;

            staticCollisionCount++;
            director?.ReleaseAttackToken(this);
            WeaponPermission = EdpcgWeaponPermission.Safe;
            attackState = AttackState.Recovering;
            attackStateUntil = Time.time + 1.2f;
            PathIntent = EdpcgPathIntent.BreakAway;
            nextSemanticDecisionAt = Time.time;
            Vector3 normal = collision.contactCount > 0
                ? collision.GetContact(0).normal
                : -transform.forward;
            desiredVelocity = Vector3.Reflect(
                body.velocity.sqrMagnitude > 1f
                    ? body.velocity.normalized
                    : transform.forward,
                normal) * profile.maximumSpeed * 0.55f;
            if (director?.EdpcgRuntime != null &&
                staticCollisionCount >= 3)
            {
                BeginNavigationRecovery("repeated-static-collision");
            }
        }

        void ApplyEnvironmentalCollisionImpact(Collision collision)
        {
            float speed = collision.relativeVelocity.magnitude;
            float excess = Mathf.Max(0f, speed - 11f);
            if (excess <= 0f)
                return;
            float damage = Mathf.Clamp(
                excess * excess * 2.1f * environmentalImpactScale,
                24f,
                profile.maximumHealth * 1.25f);
            float applied = Mathf.Min(health, damage);
            health = Mathf.Max(0f, health - damage);
            director?.NotifyEnemyDamaged(applied);
            environmentalImpactArmedUntil = -1f;
            if (health > 0f)
                return;
            ContactPoint contact = collision.contactCount > 0
                ? collision.GetContact(0)
                : default;
            Die(
                new SpaceDamageInfo(
                    damage,
                    contact.point,
                    collision.impulse,
                    SpaceDamageType.Collision,
                    environmentalImpactSource),
                applied,
                EdpcgEnemyResolutionReason.PlayerCausedEnvironment);
        }

        void Detonate()
        {
            if (!IsCombatCapable || !IsSuicide)
                return;
            Vector3 point = body.worldCenterOfMass;
            director?.ReleaseAttackToken(this);
            visuals?.SpawnImpact(
                point,
                Vector3.up,
                new Color(1f, 0.16f, 0.02f, 1f),
                "HordeSuicideExplosion",
                profile.detonationRadius);
            WeaponDamageUtility.ApplyExplosion(
                point,
                profile.detonationRadius,
                profile.detonationDamage,
                transform,
                gameObject,
                affectUrbanStructures:
                    HordeUrbanCombatPolicy.CanDamageUrbanStructures);
            Die(
                new SpaceDamageInfo(
                    health,
                    point,
                    Vector3.zero,
                    SpaceDamageType.Explosion,
                    gameObject),
                0f,
                EdpcgEnemyResolutionReason.SelfDetonated);
        }

        void ConfigureForProfile()
        {
            body.mass = profile.massKg;
            body.centerOfMass = new Vector3(0f, 0f, -0.6f);
            body.ResetInertiaTensor();
            float scale = profile.role == HordeEnemyRole.Gunship
                ? 1.25f
                : profile.role == HordeEnemyRole.Striker ? 1.08f : 0.92f;
            SelectRoleVisual(profile.role);
            if (activeHullVisual == fallbackHullVisual &&
                fallbackHullVisual != null)
            {
                fallbackHullVisual.transform.localScale = Vector3.one * scale;
                hullMaterial.color = profile.hullColor;
            }
            ApplyActiveHullPaint(IsSuicide);
            rootCollider.size = new Vector3(7f, 2.5f, 10f) * scale;
            weaponProfile = WeaponProfileLibrary.Resolve(null);
            weaponProfile.delivery = WeaponDeliveryKind.PhysicalProjectile;
            weaponProfile.damage = profile.shotDamage;
            weaponProfile.shotsPerSecond = 4f;
            weaponProfile.projectileSpeed = 240f;
            weaponProfile.range = 600f;
            weaponProfile.explosionRadius = 0f;
            weaponProfile.effectColor = new Color(1f, 0.18f, 0.04f, 1f);
            weaponProfile.projectileEffect =
                "sfx/mc/machinegun_bullet_shoot.sfx";
        }

        void ConfigureReengagementAudio()
        {
            reengagementAudio = GetComponent<AudioSource>();
            if (reengagementAudio == null)
                reengagementAudio = gameObject.AddComponent<AudioSource>();
            reengagementLowPass = GetComponent<AudioLowPassFilter>();
            if (reengagementLowPass == null)
            {
                reengagementLowPass =
                    gameObject.AddComponent<AudioLowPassFilter>();
            }
            if (reengagementClip == null)
                reengagementClip = CreateReengagementClip();
            reengagementAudio.clip = reengagementClip;
            reengagementAudio.playOnAwake = false;
            reengagementAudio.loop = true;
            reengagementAudio.spatialBlend = 1f;
            reengagementAudio.dopplerLevel = 0.65f;
            reengagementAudio.spread = 18f;
            reengagementAudio.rolloffMode = AudioRolloffMode.Logarithmic;
            reengagementAudio.minDistance = 22f;
            reengagementAudio.maxDistance = 260f;
            reengagementAudio.volume = 0f;
            reengagementLowPass.cutoffFrequency = 1200f;
            reengagementLowPass.lowpassResonanceQ = 1.1f;
        }

        void ResetReengagementAudio()
        {
            if (reengagementAudio == null)
                return;
            reengagementAudio.Stop();
            reengagementAudio.volume = 0f;
            reengagementAudio.pitch = ResolveReengagementPitch();
            if (reengagementLowPass != null)
                reengagementLowPass.cutoffFrequency = 1200f;
        }

        void UpdateReengagementAudio(bool shouldApproach)
        {
            if (reengagementAudio == null)
                return;
            float roleVolume = profile != null &&
                               profile.role == HordeEnemyRole.Gunship
                ? 0.34f
                : profile != null && profile.role == HordeEnemyRole.Striker
                    ? 0.29f
                    : 0.25f;
            float targetVolume = shouldApproach ? roleVolume : 0f;
            reengagementAudio.volume = Mathf.MoveTowards(
                reengagementAudio.volume,
                targetVolume,
                Time.deltaTime * (shouldApproach ? 0.55f : 0.8f));
            reengagementAudio.pitch = ResolveReengagementPitch();
            if (shouldApproach && !reengagementAudio.isPlaying)
                reengagementAudio.Play();
            if (!shouldApproach && reengagementAudio.volume <= 0.001f &&
                reengagementAudio.isPlaying)
            {
                reengagementAudio.Stop();
            }
            if (reengagementLowPass == null || playerBody == null ||
                director == null)
            {
                return;
            }
            bool clear = director.HasLineOfTravel(
                body != null ? body.worldCenterOfMass : transform.position,
                playerBody.worldCenterOfMass);
            float cutoff = clear ? 4200f : 1150f;
            reengagementLowPass.cutoffFrequency = Mathf.MoveTowards(
                reengagementLowPass.cutoffFrequency,
                cutoff,
                Time.deltaTime * 5200f);
        }

        float ResolveReengagementPitch()
        {
            float rolePitch = profile != null &&
                              profile.role == HordeEnemyRole.Gunship
                ? 0.78f
                : profile != null && profile.role == HordeEnemyRole.Striker
                    ? 0.94f
                    : 1.08f;
            float speedFraction = body != null && profile != null
                ? Mathf.Clamp01(body.velocity.magnitude /
                                Mathf.Max(1f, profile.maximumSpeed))
                : 0f;
            return rolePitch + speedFraction * 0.08f;
        }

        void StopReengagementAudio()
        {
            if (reengagementAudio == null)
                return;
            reengagementAudio.Stop();
            reengagementAudio.volume = 0f;
        }

        static AudioClip CreateReengagementClip()
        {
            const int sampleRate = 22050;
            const int sampleCount = sampleRate;
            var samples = new float[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                float phase = Mathf.PI * 2f * index / sampleCount;
                float turbine =
                    Mathf.Sin(phase * 74f) * 0.55f +
                    Mathf.Sin(phase * 148f) * 0.25f +
                    Mathf.Sin(phase * 222f) * 0.12f;
                float modulation = 0.76f +
                                   Mathf.Sin(phase * 3f) * 0.24f;
                samples[index] = turbine * modulation * 0.18f;
            }
            AudioClip clip = AudioClip.Create(
                "EnemyReengagementTurbine",
                sampleCount,
                1,
                sampleRate,
                false);
            clip.hideFlags = HideFlags.DontSave;
            clip.SetData(samples, 0);
            return clip;
        }

        void BuildHull()
        {
            GameObject hull = new GameObject("HullVisual");
            fallbackHullVisual = hull;
            hull.transform.SetParent(transform, false);
            CreateHullPart(
                hull.transform,
                "Fuselage",
                PrimitiveType.Cube,
                new Vector3(0f, 0f, 0f),
                new Vector3(3.2f, 1.8f, 8.5f));
            CreateHullPart(
                hull.transform,
                "Wing",
                PrimitiveType.Cube,
                new Vector3(0f, -0.15f, -0.4f),
                new Vector3(10f, 0.35f, 3.2f));
            CreateHullPart(
                hull.transform,
                "Nose",
                PrimitiveType.Sphere,
                new Vector3(0f, 0f, 4.2f),
                new Vector3(2.4f, 1.4f, 3.4f));
            CreateHullPart(
                hull.transform,
                "Tail",
                PrimitiveType.Cube,
                new Vector3(0f, 0.9f, -3.4f),
                new Vector3(0.5f, 2.2f, 2.2f));
        }

        void CreateHullPart(
            Transform parent,
            string partName,
            PrimitiveType primitive,
            Vector3 localPosition,
            Vector3 localScale)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = hullMaterial;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                if (Application.isPlaying)
                    Destroy(collider);
                else
                    DestroyImmediate(collider);
            }
        }

        void BuildFleetVisuals()
        {
            interceptorVisual = EnemyFleetVisualLibrary.Create(
                transform,
                "hull.sf_stealth_fighter",
                "HullVisual_Suicide",
                9.2f);
            strikerVisual = EnemyFleetVisualLibrary.Create(
                transform,
                "hull.sf_fighter_gr2",
                "HullVisual_Striker",
                10.8f);
            gunshipVisual = EnemyFleetVisualLibrary.Create(
                transform,
                "hull.sf_dropship_r35",
                "HullVisual_Gunship",
                12.5f);
        }

        void SelectRoleVisual(HordeEnemyRole role)
        {
            SetVisualActive(fallbackHullVisual, false);
            SetVisualActive(interceptorVisual, false);
            SetVisualActive(strikerVisual, false);
            SetVisualActive(gunshipVisual, false);

            switch (role)
            {
                case HordeEnemyRole.Striker:
                    activeHullVisual = strikerVisual;
                    break;
                case HordeEnemyRole.Gunship:
                    activeHullVisual = gunshipVisual;
                    break;
                default:
                    activeHullVisual = interceptorVisual;
                    break;
            }
            if (activeHullVisual == null)
                activeHullVisual = fallbackHullVisual;
            SetVisualActive(activeHullVisual, true);
            renderers = activeHullVisual == null
                ? Array.Empty<Renderer>()
                : activeHullVisual.GetComponentsInChildren<Renderer>(true);
        }

        void ApplyActiveHullPaint(bool suicidePaint)
        {
            if (hullPaint == null)
                hullPaint = new MaterialPropertyBlock();
            Color redPaint = new Color(1f, 0.055f, 0.025f, 1f);
            Color redEmission = new Color(2.4f, 0.025f, 0.008f, 1f);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null)
                    continue;
                hullPaint.Clear();
                if (suicidePaint)
                {
                    Material material = renderer.sharedMaterial;
                    if (material != null && material.HasProperty("_BaseColor"))
                        hullPaint.SetColor("_BaseColor", redPaint);
                    if (material != null && material.HasProperty("_Color"))
                        hullPaint.SetColor("_Color", redPaint);
                    if (material != null && material.HasProperty("_EmissionColor"))
                        hullPaint.SetColor("_EmissionColor", redEmission);
                }
                renderer.SetPropertyBlock(hullPaint);
            }
        }

        static void SetVisualActive(GameObject visual, bool value)
        {
            if (visual != null && visual.activeSelf != value)
                visual.SetActive(value);
        }

        Bounds ResolveBounds()
        {
            if (renderers.Length == 0)
                return new Bounds(body.position, new Vector3(7f, 3f, 10f));
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                if (renderers[index] != null)
                    bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        void SetRenderers(bool enabled)
        {
            for (int index = 0; index < renderers.Length; index++)
                if (renderers[index] != null)
                    renderers[index].enabled = enabled;
        }

        Vector3 TorqueForAngularAcceleration(Vector3 acceleration)
        {
            Quaternion principalToWorld = body.rotation *
                                          body.inertiaTensorRotation;
            Vector3 principal = Quaternion.Inverse(principalToWorld) *
                                acceleration;
            return principalToWorld * Vector3.Scale(
                body.inertiaTensor,
                principal);
        }
    }
}
