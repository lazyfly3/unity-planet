using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.CombatMap;

/// <summary>
/// Replaces only the visual/collision terrain layer of a finite planet
/// mission with the city PCG. Existing mission objectives, horde director,
/// wave timing and enemy types remain owned by the formal combat systems.
/// </summary>
[DisallowMultipleComponent]
public sealed class FinitePlanetUrbanCombatRuntime : MonoBehaviour
{
    public const string TemplateResourcePath =
        "PlanetSurface/UrbanCombatCityTemplate";

    InfinitePlanarSurfaceWorld world;
    PlanetLabInfiniteTerrainStreamer streamer;
    GameObject cityRoot;
    AirCombatCityPcgLab cityLab;
    UrbanEnvironmentalFieldDirector environmentalFields;

    public bool IsReady { get; private set; }
    public string PreparationError { get; private set; } = string.Empty;
    public float GroundHeight { get; private set; }
    public int CityBuildingCount { get; private set; }
    public int SyncedEnemyIngressCount { get; private set; }
    public AirCombatCityPlan Plan => cityLab != null ? cityLab.Plan : null;
    public UrbanEnvironmentalFieldDirector EnvironmentalFields =>
        environmentalFields;

    public bool Configure(InfinitePlanarSurfaceWorld targetWorld)
    {
        IsReady = false;
        PreparationError = string.Empty;
        world = targetWorld;
        if (world == null || !world.IsFiniteCombatArea)
            return Fail("城市战场需要有限星球战斗区域。");
        if (PlanetOrbitChapterSelectionContext.EnvironmentKind !=
            PlanetMissionEnvironmentKind.Urban)
        {
            return Fail("当前任务没有选择城市作战环境。");
        }

        FinitePlanetCombatTerrainPlan terrainPlan =
            world.FiniteCombatTerrainPlan;
        if (terrainPlan == null || terrainPlan.DefenseLayout == null)
            return Fail("城市战场缺少正式任务的防御布局。");

        GameObject template =
            Resources.Load<GameObject>(TemplateResourcePath);
        if (template == null)
            return Fail("没有找到城市战场运行时模板。");

        cityRoot = Instantiate(template, transform, false);
        cityRoot.name = "UrbanCombatCity_城市任务地图";
        cityLab = cityRoot.GetComponent<AirCombatCityPcgLab>();
        if (cityLab == null)
        {
            Destroy(cityRoot);
            cityRoot = null;
            return Fail("城市战场模板缺少 AirCombatCityPcgLab。");
        }

        GroundHeight = terrainPlan.BaseGroundHeight;
        cityRoot.transform.localPosition =
            new Vector3(0f, GroundHeight, 0f);
        bool bossMission = string.Equals(
            PlanetOrbitChapterSelectionContext.MissionId,
            "modular_boss",
            System.StringComparison.Ordinal);
        AirCombatCityMission mission = bossMission
            ? AirCombatCityMission.BossEncounter
            : terrainPlan.MissionKind ==
              FinitePlanetCombatMissionTerrainKind.Assault
                ? AirCombatCityMission.FacilityAssault
                : AirCombatCityMission.Clearance;
        cityLab.ConfigureRuntimeMission(
            PlanetOrbitChapterSelectionContext.MissionSeed,
            mission,
            PlanetOrbitChapterSelectionContext.PlanetDifficultyIndex);
        if (!cityLab.HasValidPlan || cityLab.Plan == null)
        {
            string summary = cityLab.LastSummary;
            Destroy(cityRoot);
            cityRoot = null;
            cityLab = null;
            return Fail("城市 PCG 未通过约束：" + summary);
        }

        CityBuildingCount = cityLab.Plan.buildings.Count;
        SynchronizeFormalMissionAnchors(
            terrainPlan.DefenseLayout,
            cityLab.Plan);
        CreateFlatGroundCollision(cityRoot.transform);
        environmentalFields =
            cityRoot.GetComponentInChildren<
                UrbanEnvironmentalFieldDirector>(true) ??
            cityRoot.AddComponent<UrbanEnvironmentalFieldDirector>();
        environmentalFields.Configure(cityLab.Plan, cityLab.Settings);
        if (!environmentalFields.HasRequiredCombatTraps)
        {
            string validation = environmentalFields.ValidationError;
            Destroy(cityRoot);
            cityRoot = null;
            cityLab = null;
            environmentalFields = null;
            return Fail(string.IsNullOrWhiteSpace(validation)
                ? "城市 PCG 没有生成完整的风场和三面磁墙战术路线。"
                : validation);
        }

        // The inactive prefab prevents its edit-lab OnEnable path from
        // generating twice. The formal runtime owns all input and HUD after
        // activation, so the lab component remains disabled.
        cityLab.enabled = false;
        cityRoot.SetActive(true);

        streamer = world.Streamer;
        if (streamer != null)
        {
            SuppressNaturalTerrainChunks();
            streamer.ChunkActivated += HandleChunkActivated;
        }
        IsReady = true;
        return true;
    }

    public Vector3 ProjectToGround(Vector3 nearWorldPosition)
    {
        if (world == null)
            return nearWorldPosition;
        PlanarSurfaceAddress address =
            world.ToPersistentAddress(nearWorldPosition);
        return world.FromPersistentAddress(
            new PlanarSurfaceAddress(
                address.x,
                address.z,
                GroundHeight));
    }

    public Vector3 ProjectPlanPosition(Vector3 planPosition)
    {
        if (world == null)
            return planPosition;
        return world.FromPersistentAddress(
            new PlanarSurfaceAddress(
                planPosition.x,
                planPosition.z,
                GroundHeight));
    }

    public bool TryResolveBossRoadSpawn(
        Vector3 playerWorldPosition,
        float preferredDistance,
        float altitude,
        out Vector3 spawnWorldPosition,
        out Vector3 roadDirection)
    {
        spawnWorldPosition = Vector3.zero;
        roadDirection = Vector3.forward;
        if (world == null || cityLab == null || cityLab.Plan == null)
            return false;
        PlanarSurfaceAddress playerAddress =
            world.ToPersistentAddress(playerWorldPosition);
        Vector3 playerPlanPosition = new Vector3(
            (float)playerAddress.x,
            0f,
            (float)playerAddress.z);
        if (!ModularBossRoadSpawnPolicy.TryResolve(
                cityLab.Plan.roads,
                playerPlanPosition,
                preferredDistance,
                out Vector3 spawnPlanPosition,
                out roadDirection))
        {
            return false;
        }
        spawnWorldPosition = ProjectPlanPosition(spawnPlanPosition) +
                             Vector3.up * Mathf.Max(0f, altitude);
        return true;
    }

    bool Fail(string message)
    {
        PreparationError = message ?? string.Empty;
        Debug.LogError(
            "[UrbanCombat] " + PreparationError,
            this);
        return false;
    }

    void SynchronizeFormalMissionAnchors(
        FinitePlanetDefenseLayoutPlan layout,
        AirCombatCityPlan city)
    {
        layout.combatCenter = city.objective + Vector3.up * GroundHeight;
        layout.playerSpawn = city.playerSpawn + Vector3.up * GroundHeight;

        SyncedEnemyIngressCount = 0;
        if (layout.enemyIngresses != null && city.ingresses.Count > 0)
        {
            for (int index = 0;
                 index < layout.enemyIngresses.Length;
                 index++)
            {
                CombatSemanticAnchor anchor =
                    layout.enemyIngresses[index];
                if (anchor == null)
                    continue;
                int cityIndex = Mathf.FloorToInt(
                    index * city.ingresses.Count /
                    (float)layout.enemyIngresses.Length);
                cityIndex = Mathf.Clamp(
                    cityIndex,
                    0,
                    city.ingresses.Count - 1);
                AirCombatEnemyIngress source = city.ingresses[cityIndex];
                anchor.position =
                    source.position + Vector3.up * GroundHeight;
                Vector3 forward = source.target - source.position;
                if (forward.sqrMagnitude > 0.001f)
                    anchor.forward = forward.normalized;
                SyncedEnemyIngressCount++;
            }
        }

        if (layout.powerPositions == null ||
            city.facilityCores.Count == 0)
        {
            return;
        }
        int coreCount = Mathf.Min(
            layout.powerPositions.Length,
            city.facilityCores.Count);
        for (int index = 0; index < coreCount; index++)
        {
            CombatSemanticAnchor anchor = layout.powerPositions[index];
            if (anchor == null)
                continue;
            Vector3 core = city.facilityCores[index];
            Vector3 outward = core - city.objective;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.001f)
                outward = Quaternion.Euler(0f, index * 120f, 0f) *
                          Vector3.forward;
            // The city generator places a facility building at each core.
            // Put the formal destructible objective just outside that facade
            // so it remains visible and shootable without changing its rules.
            anchor.position = core + outward.normalized * 40f;
            anchor.position += Vector3.up * GroundHeight;
            anchor.forward = -outward.normalized;
        }
    }

    static void CreateFlatGroundCollision(Transform parent)
    {
        var ground = new GameObject(
            "UrbanCombatGroundCollision_城市平坦地面");
        ground.transform.SetParent(parent, false);
        ground.transform.localPosition = new Vector3(0f, -1.1f, 0f);
        BoxCollider collider = ground.AddComponent<BoxCollider>();
        collider.size = new Vector3(1792f, 2f, 1792f);
    }

    void HandleChunkActivated(Vector2Int coordinate)
    {
        SuppressNaturalTerrainChunks();
    }

    void SuppressNaturalTerrainChunks()
    {
        if (streamer == null)
            return;
        Transform[] children =
            streamer.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < children.Length; index++)
        {
            Transform child = children[index];
            if (child.name == "Terrain")
            {
                MeshRenderer renderer =
                    child.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.enabled = false;
                MeshCollider collider = child.GetComponent<MeshCollider>();
                if (collider != null)
                    collider.enabled = false;
            }
            else if (child.name == "Ocean")
            {
                MeshRenderer renderer =
                    child.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.enabled = false;
            }
        }
    }

    void OnDestroy()
    {
        if (streamer != null)
            streamer.ChunkActivated -= HandleChunkActivated;
    }
}
