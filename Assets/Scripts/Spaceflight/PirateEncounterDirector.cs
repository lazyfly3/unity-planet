using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PirateEncounterDirector : MonoBehaviour
{
    [Header("Encounter")]
    [SerializeField] bool encountersEnabled = true;
    [SerializeField] GameObject pirateShipPrefab;
    [SerializeField] InterstellarFlightRuntime flightRuntime;
    [SerializeField] Transform enemyRoot;
    [SerializeField, Range(1, 6)] int maximumActiveEnemies = 3;
    [SerializeField, Min(0f)] float initialSpawnDelay = 2f;
    [SerializeField, Min(1f)] float spawnInterval = 18f;
    [SerializeField, Min(10f)] float firstSpawnMinimumDistance = 65f;
    [SerializeField, Min(10f)] float firstSpawnMaximumDistance = 85f;
    [SerializeField, Min(10f)] float minimumSpawnDistance = 80f;
    [SerializeField, Min(10f)] float maximumSpawnDistance = 120f;
    [SerializeField, Min(200f)] float despawnDistance = 2400f;
    [SerializeField] int seedOffset = 19381;

    readonly List<GameObject> activeEnemies = new List<GameObject>(6);
    Rigidbody playerBody;
    SpaceCombatant playerCombatant;
    float nextSpawnTime;
    int encounterIndex;

    public bool EncountersEnabled => encountersEnabled;
    public int ActiveEnemyCount => activeEnemies.Count;

    void Awake()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();
        if (flightRuntime != null)
        {
            flightRuntime.OriginShifted += HandleOriginShift;
            flightRuntime.UniverseRelocated += HandleUniverseRelocated;
        }
        nextSpawnTime = Time.time + initialSpawnDelay;
    }

    void Update()
    {
        if (!encountersEnabled)
            return;

        ResolvePlayer();
        RemoveInvalidOrDistantEnemies();
        if (playerBody == null || activeEnemies.Count >= maximumActiveEnemies || Time.time < nextSpawnTime)
            return;

        SpawnNextEnemy();
        nextSpawnTime = Time.time + spawnInterval;
    }

    public void SetEncountersEnabled(bool value)
    {
        encountersEnabled = value;
        if (value)
            nextSpawnTime = Time.time + Mathf.Min(1f, initialSpawnDelay);
    }

    public PirateEncounterPlan CreatePlan(int worldSeed, int index)
    {
        ulong value = Hash(unchecked((uint)(worldSeed + seedOffset)), unchecked((uint)index));
        if (index == 0)
        {
            float yaw = Mathf.Lerp(45f, 75f, ToUnit(value));
            if (((value >> 20) & 1UL) != 0UL)
                yaw = -yaw;
            float firstElevation = Mathf.Lerp(15f, 25f, ToUnit(value >> 21));
            if (((value >> 41) & 1UL) != 0UL)
                firstElevation = -firstElevation;
            float firstDistance = Mathf.Lerp(
                Mathf.Min(firstSpawnMinimumDistance, firstSpawnMaximumDistance),
                Mathf.Max(firstSpawnMinimumDistance, firstSpawnMaximumDistance),
                ToUnit(value >> 42));
            return new PirateEncounterPlan
            {
                seed = unchecked((int)(value ^ (value >> 32))),
                tier = 1 + (int)((value >> 61) % 3UL),
                localSpawnOffset = Quaternion.Euler(-firstElevation, yaw, 0f) * Vector3.forward * firstDistance
            };
        }

        float azimuth = ToUnit(value) * Mathf.PI * 2f;
        float elevation = Mathf.Lerp(-0.42f, 0.42f, ToUnit(value >> 21));
        float distance = Mathf.Lerp(
            Mathf.Min(minimumSpawnDistance, maximumSpawnDistance),
            Mathf.Max(minimumSpawnDistance, maximumSpawnDistance),
            ToUnit(value >> 42));
        float planar = Mathf.Cos(elevation);
        return new PirateEncounterPlan
        {
            seed = unchecked((int)(value ^ (value >> 32))),
            tier = 1 + (int)((value >> 61) % 3UL),
            localSpawnOffset = new Vector3(
                Mathf.Sin(azimuth) * planar,
                Mathf.Sin(elevation),
                Mathf.Cos(azimuth) * planar) * distance
        };
    }

    public bool SpawnNextEnemy()
    {
        ResolveReferences();
        ResolvePlayer();
        if (playerBody == null || playerCombatant == null)
            return false;
        if (pirateShipPrefab == null)
            pirateShipPrefab = Resources.Load<GameObject>("Spaceflight/Pirates/ProceduralPirateShip");
        if (pirateShipPrefab == null)
        {
            Debug.LogError("PirateEncounterDirector: ProceduralPirateShip prefab is missing.", this);
            encountersEnabled = false;
            return false;
        }

        int worldSeed = GalaxyTravelManager.Instance == null ? 0 : GalaxyTravelManager.Instance.WorldSeed;
        PirateEncounterPlan plan = CreatePlan(worldSeed, encounterIndex++);
        Vector3 spawnPosition = playerBody.position + playerBody.rotation * plan.localSpawnOffset;
        Vector3 forward = playerBody.worldCenterOfMass - spawnPosition;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, forward);
        if (up.sqrMagnitude < 0.001f)
            up = Vector3.ProjectOnPlane(Vector3.right, forward);
        Quaternion rotation = Quaternion.LookRotation(forward.normalized, up.normalized);

        GameObject enemy = Instantiate(pirateShipPrefab, spawnPosition, rotation, enemyRoot);
        enemy.name = $"Pirate_{plan.seed}_{plan.tier}";
        ProceduralPirateShipGenerator generator = enemy.GetComponent<ProceduralPirateShipGenerator>();
        if (generator == null || !generator.Generate(plan.seed, plan.tier))
        {
            Destroy(enemy);
            return false;
        }

        PirateShipAiController ai = enemy.GetComponent<PirateShipAiController>();
        if (ai == null)
            ai = enemy.AddComponent<PirateShipAiController>();
        ai.Configure(playerCombatant, plan.seed, plan.tier);

        PirateShipLifecycle lifecycle = enemy.GetComponent<PirateShipLifecycle>();
        if (lifecycle == null)
            lifecycle = enemy.AddComponent<PirateShipLifecycle>();
        lifecycle.Configure(this);

        activeEnemies.Add(enemy);
        return true;
    }

    public int GetActiveThreatsNonAlloc(PirateShipAiController[] buffer)
    {
        if (buffer == null || buffer.Length == 0)
            return 0;
        int count = 0;
        for (int index = 0; index < activeEnemies.Count && count < buffer.Length; index++)
        {
            GameObject enemy = activeEnemies[index];
            if (enemy == null)
                continue;
            PirateShipAiController ai = enemy.GetComponent<PirateShipAiController>();
            if (ai == null || !ai.isActiveAndEnabled || !ai.IsActivelyAttacking)
                continue;
            buffer[count++] = ai;
        }
        for (int index = count; index < buffer.Length; index++)
            buffer[index] = null;
        return count;
    }

    public void NotifyEnemyDestroyed(GameObject enemy)
    {
        if (enemy != null)
            activeEnemies.Remove(enemy);
        nextSpawnTime = Mathf.Max(nextSpawnTime, Time.time + spawnInterval);
    }

    void RemoveInvalidOrDistantEnemies()
    {
        float maximumDistanceSquared = despawnDistance * despawnDistance;
        for (int index = activeEnemies.Count - 1; index >= 0; index--)
        {
            GameObject enemy = activeEnemies[index];
            if (enemy == null)
            {
                activeEnemies.RemoveAt(index);
                continue;
            }
            if (playerBody == null || (enemy.transform.position - playerBody.position).sqrMagnitude <= maximumDistanceSquared)
                continue;
            activeEnemies.RemoveAt(index);
            Destroy(enemy);
        }
    }

    void ResolveReferences()
    {
        if (flightRuntime == null)
            flightRuntime = FindObjectOfType<InterstellarFlightRuntime>();
        if (enemyRoot == null)
            enemyRoot = GameObject.Find("EnemyRuntimeRoot")?.transform;
        ResolvePlayer();
    }

    void ResolvePlayer()
    {
        if (playerBody == null && flightRuntime != null)
            playerBody = flightRuntime.ShipBody;
        if (playerCombatant == null && playerBody != null)
        {
            playerCombatant = playerBody.GetComponent<SpaceCombatant>();
            if (playerCombatant == null)
            {
                playerCombatant = playerBody.gameObject.AddComponent<SpaceCombatant>();
                playerCombatant.Configure(SpaceCombatFaction.Player, playerBody.transform, true);
            }
        }
    }

    void HandleOriginShift(Vector3 shift)
    {
        for (int index = 0; index < activeEnemies.Count; index++)
        {
            GameObject enemy = activeEnemies[index];
            if (enemy == null)
                continue;
            Rigidbody body = enemy.GetComponent<Rigidbody>();
            if (body == null)
            {
                enemy.transform.position -= shift;
                continue;
            }
            RigidbodyInterpolation interpolation = body.interpolation;
            body.interpolation = RigidbodyInterpolation.None;
            body.position -= shift;
            body.interpolation = interpolation;
        }
        Physics.SyncTransforms();
    }

    void HandleUniverseRelocated(DoubleVector3 previousPosition, DoubleVector3 currentPosition)
    {
        for (int index = activeEnemies.Count - 1; index >= 0; index--)
        {
            if (activeEnemies[index] != null)
                Destroy(activeEnemies[index]);
        }
        activeEnemies.Clear();
        nextSpawnTime = Time.time + Mathf.Max(5f, spawnInterval);
    }

    void OnDisable()
    {
        if (flightRuntime != null)
        {
            flightRuntime.OriginShifted -= HandleOriginShift;
            flightRuntime.UniverseRelocated -= HandleUniverseRelocated;
        }
    }

    static ulong Hash(uint seed, uint index)
    {
        ulong value = seed | ((ulong)index << 32);
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    static float ToUnit(ulong value)
    {
        return (value & 0x1fffffUL) / 2097151f;
    }
}
