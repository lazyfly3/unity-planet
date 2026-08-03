using System;
using System.Collections;
using System.Collections.Generic;
using ModularAssembly;
using UnityEngine;

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

    public enum HordeFormationKind
    {
        V,
        LineAbreast,
        Pincer,
        Echelon
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
                        preferredMinimumRange = 55f,
                        preferredMaximumRange = 85f,
                        shotDamage = 8f,
                        gunCount = 1,
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
                if ((point - start).sqrMagnitude > 16f)
                    result.Add(point);
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
            public float ReadyAt;
        }

        const int PoolSize = 10;
        const float DurationSeconds = 240f;
        const float SharedAttackCooldown = 0.45f;
        readonly List<HordeEnemyVehicle> pool =
            new List<HordeEnemyVehicle>(PoolSize);
        readonly List<HordeEnemyVehicle> active =
            new List<HordeEnemyVehicle>(PoolSize);
        readonly Queue<QueuedSpawn> spawnQueue =
            new Queue<QueuedSpawn>(24);
        readonly Collider[] spawnOverlaps = new Collider[32];
        CombatTestController owner;
        WeaponVisualPool visuals;
        WeaponProjectilePool projectiles;
        Rigidbody playerBody;
        HordeAirNavigationService navigation;
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
        int lastSector = -1;
        HordeFormationKind lastFormation = (HordeFormationKind)(-1);
        bool running;
        bool finalClear;

        public bool PreparationValid { get; private set; }
        public string PreparationError { get; private set; } = string.Empty;
        public bool NavigationReady => navigation != null && navigation.IsReady;
        public string NavigationError => navigation?.FailureReason ?? string.Empty;
        public float Elapsed => elapsed;
        public float RemainingSeconds => Mathf.Max(0f, DurationSeconds - elapsed);
        public int Kills { get; private set; }
        public int Escaped { get; private set; }
        public int PeakActive { get; private set; }
        public int QueuedCount => spawnQueue.Count;
        public int SessionId => sessionId;
        public bool IsFinalClear => finalClear;
        public bool IsRunning => running;
        public bool IsFinished => finalClear && AliveCount == 0;

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

        public int CurrentPhase => PhaseAt(elapsed);
        public int CurrentActiveCap => ActiveCapAt(elapsed);

        public static int PhaseAt(float seconds)
        {
            return seconds < 45f ? 1 : seconds < 180f ? 2 : 3;
        }

        public static int ActiveCapAt(float seconds)
        {
            return seconds < 45f ? 4 : seconds < 180f ? 8 : 10;
        }

        public static float SpawnIntervalAt(float seconds)
        {
            return seconds < 45f ? 8f : seconds < 180f ? 6f : 5f;
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

            if (owner == null || visuals == null || projectiles == null)
            {
                PreparationError = "割草战斗缺少武器或特效池。";
                yield break;
            }

            for (int index = pool.Count; index < PoolSize; index++)
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
                    (index + 1f) / PoolSize,
                    "正在预创建割草敌机 " + (index + 1) + "/" + PoolSize);
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
            PreparationValid = pool.Count == PoolSize;
            progress?.Invoke(1f, "割草战斗资源已就绪");
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
            lastSector = -1;
            lastFormation = (HordeFormationKind)(-1);
            Kills = 0;
            Escaped = 0;
            PeakActive = 0;
            finalClear = false;
            running = true;
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

            elapsed = Mathf.Min(DurationSeconds, elapsed + Mathf.Max(0f, deltaTime));
            if (elapsed >= DurationSeconds)
            {
                finalClear = true;
                spawnQueue.Clear();
            }
            else if (Time.time >= nextSpawnAt && !InReliefWindow(elapsed))
            {
                QueueSpawnBeat();
                nextSpawnAt = Time.time + SpawnIntervalAt(elapsed);
            }

            int cap = ActiveCapAt(elapsed);
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
            if (enemy == null || playerBody == null)
                return battleCenter;
            HordeEnemyProfile profile = enemy.Profile;
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
            int limit = CurrentPhase == 1 ? 1 : 2;
            int cost = enemy.Profile.attackTokenCost;
            if (attackTokens + cost > limit || Time.time < nextAttackAt)
                return false;
            attackTokens += cost;
            nextAttackAt = Time.time + SharedAttackCooldown;
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

        public void NotifyEnemyDeath(HordeEnemyVehicle enemy, float damage)
        {
            ReleaseAttackToken(enemy);
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
            Escaped++;
        }

        void QueueSpawnBeat()
        {
            int phase = CurrentPhase;
            int budget = phase == 1 ? 2 : phase == 2 ? 3 : 4;
            HordeFormationKind formation = NextFormation(phase);
            int sector = NextSector(phase);
            var roles = new HordeEnemyRole[4];
            int count = 0;

            if (phase == 3 && !HasGunship() && spawnSequence % 3 == 0)
            {
                roles[count++] = HordeEnemyRole.Gunship;
                budget -= 3;
            }
            else if (phase >= 2 && budget >= 2 && spawnSequence % 2 == 1)
            {
                roles[count++] = HordeEnemyRole.Striker;
                budget -= 2;
            }
            else if (phase == 1 && spawnSequence % 4 == 3)
            {
                roles[count++] = HordeEnemyRole.Striker;
                budget -= 2;
            }

            while (budget > 0 && count < roles.Length)
            {
                roles[count++] = HordeEnemyRole.Interceptor;
                budget--;
            }
            for (int index = 0; index < count; index++)
            {
                spawnQueue.Enqueue(new QueuedSpawn
                {
                    Role = roles[index],
                    Formation = formation,
                    Sector = sector,
                    MemberIndex = index,
                    MemberCount = count,
                    Attempts = 0,
                    ReadyAt = phase >= 2
                        ? Time.time + 1.2f
                        : Time.time
                });
            }
            lastFormation = formation;
            lastSector = sector;
            spawnSequence++;
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
                queued.Attempts++;
                spawnQueue.Enqueue(queued);
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
                sessionId);
            active.Add(enemy);
            return true;
        }

        bool TryResolveSpawnPosition(
            QueuedSpawn queued,
            out Vector3 position)
        {
            position = Vector3.zero;
            if (playerBody == null || navigation == null)
                return false;
            Vector3 forward = Vector3.ProjectOnPlane(
                playerBody.transform.forward,
                Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            forward.Normalize();
            float angle = queued.Sector * 45f + queued.Attempts * 22.5f;
            Vector3 radial = Quaternion.AngleAxis(angle, Vector3.up) * forward;
            float distance = 190f + (queued.Attempts * 23 + spawnSequence * 17) % 100;
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
            if (playerDistance < 150f || playerDistance > 320f ||
                Vector3.Distance(candidate, battleCenter) > warningRadius * 0.75f)
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

        HordeFormationKind NextFormation(int phase)
        {
            int allowed = phase == 1 ? 2 : 4;
            HordeFormationKind result;
            do
            {
                result = (HordeFormationKind)random.Next(0, allowed);
            } while (allowed > 1 && result == lastFormation);
            return result;
        }

        int NextSector(int phase)
        {
            int[] early = { -2, -1, 0, 1, 2 };
            int result;
            do
            {
                result = phase == 1
                    ? early[random.Next(0, early.Length)]
                    : random.Next(-3, 4);
            } while (result == lastSector);
            return result;
        }

        static bool InReliefWindow(float seconds)
        {
            return seconds > 8f && seconds % 24f >= 20f;
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
        Renderer[] renderers = Array.Empty<Renderer>();
        Material hullMaterial;
        HordeEnemyProfile profile;
        WeaponProfile weaponProfile;
        Vector3 desiredVelocity;
        Vector3 desiredAim;
        Vector3 lastProgressPosition;
        float health;
        float spawnAt;
        float nextReplanAt;
        float progressCheckedAt;
        float outsideSeconds;
        float attackStateUntil;
        float nextShotAt;
        float returnAt;
        int routeIndex;
        int sessionId;
        bool activeForCombat;
        bool dead;
        bool hasAttackToken;
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
            renderers = GetComponentsInChildren<Renderer>(true);
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
            int newSessionId)
        {
            profile = selectedProfile ?? HordeEnemyProfile.ForRole(
                HordeEnemyRole.Interceptor);
            playerBody = targetBody;
            SlotIndex = slotIndex;
            sessionId = newSessionId;
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
            outsideSeconds = 0f;
            attackState = AttackState.Tracking;
            attackStateUntil = spawnAt + 2f;
            nextShotAt = 0f;
            returnAt = float.PositiveInfinity;
            route.Clear();
            routeIndex = 0;
            ConfigureForProfile();

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
                Die(damage, applied);
        }

        void Update()
        {
            if (!activeForCombat || dead || playerBody == null ||
                director == null || director.SessionId != sessionId)
                return;
            UpdateNavigation();
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
            Vector3 tactical = director.ResolveTacticalDestination(this);
            bool stuck = Time.time - progressCheckedAt >= 2f &&
                         Vector3.Distance(body.position, lastProgressPosition) < 5f;
            if (Time.time >= nextReplanAt || stuck || route.Count == 0)
            {
                director.TryFindPath(body.position, tactical, route);
                routeIndex = 0;
                nextReplanAt = Time.time + 0.5f;
                progressCheckedAt = Time.time;
                lastProgressPosition = body.position;
            }
            while (routeIndex < route.Count &&
                   Vector3.Distance(body.position, route[routeIndex]) < 14f)
                routeIndex++;
            Vector3 waypoint = routeIndex < route.Count
                ? route[routeIndex]
                : tactical;
            Vector3 direction = waypoint - body.position;
            if (direction.sqrMagnitude < 0.01f)
                direction = transform.forward;
            Vector3 separation = director.SampleSeparation(this) * 20f;
            desiredVelocity = Vector3.ClampMagnitude(
                direction.normalized * profile.maximumSpeed + separation,
                profile.maximumSpeed);
            Vector3 predicted = playerBody.worldCenterOfMass +
                                playerBody.velocity * Mathf.Clamp(
                                    Vector3.Distance(
                                        body.position,
                                        playerBody.worldCenterOfMass) / 240f,
                                    0f,
                                    0.8f);
            desiredAim = (predicted - body.worldCenterOfMass).normalized;
        }

        void UpdateAttack()
        {
            Vector3 target = playerBody.worldCenterOfMass;
            float distance = Vector3.Distance(body.worldCenterOfMass, target);
            bool canEngage = Time.time >= spawnAt + 2f &&
                             distance <= 420f &&
                             director.HasLineOfTravel(body.worldCenterOfMass, target);
            switch (attackState)
            {
                case AttackState.Tracking:
                    if (canEngage && Time.time >= attackStateUntil &&
                        director.TryAcquireAttackToken(this))
                    {
                        attackState = AttackState.Telegraph;
                        attackStateUntil = Time.time + 0.55f;
                    }
                    break;
                case AttackState.Telegraph:
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
                        attackStateUntil = Time.time + 0.8f;
                        nextShotAt = 0f;
                    }
                    break;
                case AttackState.Firing:
                    if (Time.time >= nextShotAt)
                    {
                        FireVolley(target);
                        nextShotAt = Time.time + 0.25f;
                    }
                    if (Time.time >= attackStateUntil)
                    {
                        director.ReleaseAttackToken(this);
                        attackState = AttackState.Recovering;
                        attackStateUntil = Time.time + 0.9f;
                    }
                    break;
                default:
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
            returnAt = Time.time;
            director?.NotifyEnemyEscaped(this);
            rootCollider.enabled = false;
            SetRenderers(false);
            body.isKinematic = true;
        }

        void Die(SpaceDamageInfo damage, float appliedDamage)
        {
            if (dead)
                return;
            dead = true;
            activeForCombat = false;
            returnAt = Time.time + 0.35f;
            director?.NotifyEnemyDeath(this, 0f);
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
                    0));
            SetRenderers(false);
        }

        void ConfigureForProfile()
        {
            body.mass = profile.massKg;
            body.centerOfMass = new Vector3(0f, 0f, -0.6f);
            body.ResetInertiaTensor();
            float scale = profile.role == HordeEnemyRole.Gunship
                ? 1.25f
                : profile.role == HordeEnemyRole.Striker ? 1.08f : 0.92f;
            Transform hull = transform.Find("HullVisual");
            if (hull != null)
                hull.localScale = Vector3.one * scale;
            rootCollider.size = new Vector3(7f, 2.5f, 10f) * scale;
            hullMaterial.color = profile.hullColor;
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

        void BuildHull()
        {
            GameObject hull = new GameObject("HullVisual");
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
