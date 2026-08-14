using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.CombatMap;
using UnityPlanet.EDPCG;

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
    public AirCombatCityRuntimeGeometrySnapshot RuntimeGeometrySnapshot =>
        cityLab != null ? cityLab.RuntimeGeometrySnapshot : null;
    public AirCombatCitySettings CitySettings =>
        cityLab != null ? cityLab.Settings : null;
    public UrbanEnvironmentalFieldDirector EnvironmentalFields =>
        environmentalFields;

    public bool Configure(InfinitePlanarSurfaceWorld targetWorld)
    {
        if (!TryPrepareCityRuntime(
                targetWorld,
                out FinitePlanetCombatTerrainPlan terrainPlan,
                out AirCombatCityMission mission,
                out CombatCityPcgDesignProfile runtimeDesignProfile))
        {
            return false;
        }

        cityLab.ConfigureRuntimeMission(
            PlanetOrbitChapterSelectionContext.MissionSeed,
            mission,
            PlanetOrbitChapterSelectionContext.PlanetDifficultyIndex,
            runtimeDesignProfile);
        return CompleteCityRuntime(terrainPlan);
    }

    /// <summary>
    /// Formal loading path. The city planner yields while its detached data
    /// task runs so the loading canvas can animate; all scene-object creation
    /// and physics setup remain on the Unity thread.
    /// </summary>
    public IEnumerator ConfigureRoutine(
        InfinitePlanarSurfaceWorld targetWorld,
        Action<bool, string> completed)
    {
        FinitePlanetCombatTerrainPlan terrainPlan = null;
        AirCombatCityMission mission = AirCombatCityMission.Clearance;
        CombatCityPcgDesignProfile runtimeDesignProfile = null;
        bool prepared = false;
        try
        {
            prepared = TryPrepareCityRuntime(
                targetWorld,
                out terrainPlan,
                out mission,
                out runtimeDesignProfile);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            Fail("城市战场准备发生异常：" + exception.Message);
        }
        if (!prepared)
        {
            completed?.Invoke(false, PreparationError);
            yield break;
        }

        bool planningSucceeded = false;
        string planningError = string.Empty;
        IEnumerator planningRoutine = null;
        try
        {
            planningRoutine = cityLab.ConfigureRuntimeMissionRoutine(
                PlanetOrbitChapterSelectionContext.MissionSeed,
                mission,
                PlanetOrbitChapterSelectionContext.PlanetDifficultyIndex,
                runtimeDesignProfile,
                (success, error) =>
                {
                    planningSucceeded = success;
                    planningError = error ?? string.Empty;
                });
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            planningError = "城市规划无法启动：" + exception.Message;
        }

        if (planningRoutine != null)
        {
            try
            {
                while (true)
                {
                    bool hasNext = false;
                    object yielded = null;
                    try
                    {
                        hasNext = planningRoutine.MoveNext();
                        if (hasNext)
                            yielded = planningRoutine.Current;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, this);
                        planningError = "城市规划发生异常：" +
                                        exception.Message;
                    }
                    if (!hasNext || !string.IsNullOrEmpty(planningError))
                        break;
                    yield return yielded;
                }
            }
            finally
            {
                (planningRoutine as IDisposable)?.Dispose();
            }
        }

        if (!planningSucceeded)
        {
            string summary = !string.IsNullOrWhiteSpace(planningError)
                ? planningError
                : cityLab != null
                    ? cityLab.LastSummary
                    : "城市规划未返回有效结果。";
            if (cityRoot != null)
                Destroy(cityRoot);
            cityRoot = null;
            cityLab = null;
            Fail("城市 PCG 未通过约束：" + summary);
            completed?.Invoke(false, PreparationError);
            yield break;
        }

        bool configured = false;
        try
        {
            configured = CompleteCityRuntime(terrainPlan);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            Fail("城市战场实例化失败：" + exception.Message);
        }
        completed?.Invoke(configured, PreparationError);
    }

    public void CancelConfiguration()
    {
        cityLab?.CancelRuntimeMissionPlanning();
    }

    bool TryPrepareCityRuntime(
        InfinitePlanarSurfaceWorld targetWorld,
        out FinitePlanetCombatTerrainPlan terrainPlan,
        out AirCombatCityMission mission,
        out CombatCityPcgDesignProfile runtimeDesignProfile)
    {
        IsReady = false;
        PreparationError = string.Empty;
        world = targetWorld;
        terrainPlan = null;
        mission = AirCombatCityMission.Clearance;
        runtimeDesignProfile = null;
        if (world == null || !world.IsFiniteCombatArea)
            return Fail("城市战场需要有限星球战斗区域。");
        if (PlanetOrbitChapterSelectionContext.EnvironmentKind !=
            PlanetMissionEnvironmentKind.Urban)
        {
            return Fail("当前任务没有选择城市作战环境。");
        }

        terrainPlan = world.FiniteCombatTerrainPlan;
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
        mission = bossMission
            ? AirCombatCityMission.BossEncounter
            : terrainPlan.MissionKind ==
              FinitePlanetCombatMissionTerrainKind.Assault
                ? AirCombatCityMission.FacilityAssault
                : AirCombatCityMission.Clearance;
        EdpcgCityTacticalChallengeProfile tacticalChallenge = bossMission
            ? null
            : EdpcgCityTacticalChallengeProfile.LoadOrCreateMemoryDefault();
        runtimeDesignProfile = tacticalChallenge != null
            ? tacticalChallenge.cityGeometryProfile
            : null;
        return true;
    }

    bool CompleteCityRuntime(FinitePlanetCombatTerrainPlan terrainPlan)
    {
        if (!cityLab.HasValidPlan || cityLab.Plan == null)
        {
            string summary = cityLab.LastSummary;
            Destroy(cityRoot);
            cityRoot = null;
            cityLab = null;
            return Fail("城市 PCG 未通过约束：" + summary);
        }

        CityBuildingCount = cityLab.RuntimeGeometrySnapshot != null
            ? cityLab.RuntimeGeometrySnapshot.instantiatedBuildingCount
            : cityLab.Plan.buildings.Count;
        float minimumFarClip = cityLab.BuildsVisualBackground
            ? InfinitePlanarSurfaceWorld.CalculateUrbanVisualRequiredFarClip(
                cityLab.Settings.mapSize,
                world.FiniteCombatRadius,
                AirCombatCityPcgLab
                    .VisualBackgroundMaximumOutsideDistance,
                Mathf.Max(
                    cityLab.Settings.maximumAltitude,
                    AirCombatCityPcgLab
                        .VisualBackgroundMaximumBuildingHeight))
            : 0f;
        world.ApplyUrbanFlightCeiling(
            cityLab.Settings.maximumAltitude,
            minimumFarClip);
        SynchronizeFormalMissionAnchors(
            terrainPlan.DefenseLayout,
            cityLab.Plan);
        CreateFlatGroundCollision(
            cityRoot.transform,
            Mathf.Max(
                cityLab.Settings.mapSize + 128f,
                world.FiniteCombatRadius * 2f + 128f));
        environmentalFields =
            cityRoot.GetComponentInChildren<
                UrbanEnvironmentalFieldDirector>(true) ??
            cityRoot.AddComponent<UrbanEnvironmentalFieldDirector>();
        environmentalFields.Configure(cityLab.Plan, cityLab.Settings);
        if (!environmentalFields.HasRequiredCombatTraps)
        {
            string validation = environmentalFields.ValidationError;
            string warning = string.IsNullOrWhiteSpace(validation)
                ? "城市 PCG 没有生成完整的风场和三面磁墙战术路线。"
                : validation;
            if (cityLab.Report != null)
            {
                cityLab.Report.degraded = true;
                cityLab.Report.degradationWarning = string.IsNullOrWhiteSpace(
                    cityLab.Report.degradationWarning)
                    ? warning
                    : cityLab.Report.degradationWarning + " " + warning;
            }
            Debug.LogWarning("[UrbanCombat] " + warning +
                             " 城市仍可进入，环境陷阱按可用子集运行。", this);
        }

        // The inactive prefab prevents its edit-lab OnEnable path from
        // generating twice. The formal runtime owns all input and HUD after
        // activation, so the lab component remains disabled.
        cityLab.enabled = false;
        cityRoot.SetActive(true);

        streamer = world.Streamer;
        if (streamer != null)
        {
            SuppressUnderlyingTerrainChunks();
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

    /// <summary>
    /// Resolves one formal modular-facility ground anchor inside the pad
    /// reserved by the city planner. This is deliberately a geometry service only: it
    /// does not know about objectives, damage, EDPCG or player modules.
    /// </summary>
    public bool TryResolveAssaultFacilitySite(
        Vector3 planPosition,
        Vector3 requiredWorldSize,
        IReadOnlyList<Bounds> occupiedWorldBounds,
        out Vector3 worldGroundPosition,
        out Quaternion worldRotation,
        out string error)
    {
        worldGroundPosition = Vector3.zero;
        worldRotation = Quaternion.identity;
        error = string.Empty;
        if (!IsReady || cityRoot == null || Plan == null ||
            RuntimeGeometrySnapshot == null ||
            !RuntimeGeometrySnapshot.IsUsable)
        {
            error = "城市实体几何快照尚未准备完成。";
            return false;
        }

        Vector3 size = new Vector3(
            Mathf.Max(8f, requiredWorldSize.x),
            Mathf.Max(8f, requiredWorldSize.y),
            Mathf.Max(8f, requiredWorldSize.z));
        float maximumNudge = Mathf.Max(
            0f,
            (AirCombatCityGenerator.FacilityPadSize -
             Mathf.Max(size.x, size.z)) * 0.5f - 2f);
        Vector2[] directions =
        {
            Vector2.zero,
            Vector2.right, Vector2.up, Vector2.left, Vector2.down,
            new Vector2(0.7071068f, 0.7071068f),
            new Vector2(-0.7071068f, 0.7071068f),
            new Vector2(-0.7071068f, -0.7071068f),
            new Vector2(0.7071068f, -0.7071068f)
        };
        float[] distances = maximumNudge >= 6f
            ? new[] { 0f, Mathf.Min(6f, maximumNudge), maximumNudge }
            : new[] { 0f, maximumNudge };
        Vector3 ground = ProjectPlanPosition(planPosition);
        for (int distanceIndex = 0;
             distanceIndex < distances.Length;
             distanceIndex++)
        for (int directionIndex = 0;
             directionIndex < directions.Length;
             directionIndex++)
        {
            if (distanceIndex == 0 && directionIndex > 0)
                continue;
            Vector2 offset = directions[directionIndex] *
                             distances[distanceIndex];
            Vector3 candidateGround = ground +
                                      new Vector3(offset.x, 0f, offset.y);
            Vector3 candidate = candidateGround +
                                Vector3.up * (size.y * 0.5f);
            Bounds bounds = new Bounds(candidate, size);
            if (!FacilityBoundsAreClear(bounds, occupiedWorldBounds))
                continue;
            worldGroundPosition = candidateGround;
            Vector3 objectiveWorld = ProjectPlanPosition(Plan.objective);
            Vector3 forward = objectiveWorld - candidate;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            worldRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            return true;
        }

        error = "设施预留地与城市实体、航路或其他设施发生重叠。";
        return false;
    }

    bool FacilityBoundsAreClear(
        Bounds worldBounds,
        IReadOnlyList<Bounds> occupiedWorldBounds)
    {
        if (occupiedWorldBounds != null)
        {
            for (int index = 0; index < occupiedWorldBounds.Count; index++)
            {
                Bounds occupied = occupiedWorldBounds[index];
                occupied.Expand(4f);
                if (occupied.Intersects(worldBounds))
                    return false;
            }
        }

        Vector3 localCenter = cityRoot.transform.InverseTransformPoint(
            worldBounds.center);
        Bounds localBounds = new Bounds(localCenter, worldBounds.size);
        float mapHalf = CitySettings.mapSize * 0.5f - 12f;
        if (Mathf.Abs(localCenter.x) + localBounds.extents.x > mapHalf ||
            Mathf.Abs(localCenter.z) + localBounds.extents.z > mapHalf)
        {
            return false;
        }

        AirCombatRuntimeBuildingGeometry[] buildings =
            RuntimeGeometrySnapshot.buildings;
        for (int index = 0; index < buildings.Length; index++)
        {
            Bounds obstacle = buildings[index].localBounds;
            obstacle.Expand(4f);
            if (obstacle.Intersects(localBounds))
                return false;
        }

        if (ConnectionIntersectsBounds(
                RuntimeGeometrySnapshot.skybridges,
                localBounds,
                3f) ||
            ConnectionIntersectsBounds(
                RuntimeGeometrySnapshot.aerialCables,
                localBounds,
                2f) ||
            RouteIntersectsBounds(
                RuntimeGeometrySnapshot.routes,
                localBounds))
        {
            return false;
        }

        Collider[] overlaps = new Collider[128];
        int count = Physics.OverlapBoxNonAlloc(
            worldBounds.center,
            worldBounds.extents * 0.97f,
            overlaps,
            Quaternion.identity,
            ~0,
            QueryTriggerInteraction.Ignore);
        if (count >= overlaps.Length)
            return false;
        for (int index = 0; index < count; index++)
        {
            Collider hit = overlaps[index];
            if (hit == null || hit is TerrainCollider)
                continue;
            Bounds hitBounds = hit.bounds;
            if (hitBounds.max.y <= worldBounds.min.y + 0.25f ||
                hit.name.IndexOf("Ground", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                hit.name.IndexOf("地面", System.StringComparison.Ordinal) >= 0)
            {
                continue;
            }
            return false;
        }
        return true;
    }

    static bool ConnectionIntersectsBounds(
        AirCombatRuntimeConnectionGeometry[] connections,
        Bounds bounds,
        float radius)
    {
        if (connections == null)
            return false;
        for (int index = 0; index < connections.Length; index++)
        {
            Vector3 start = connections[index].localStart;
            Vector3 end = connections[index].localEnd;
            if (SegmentIntersectsExpandedBounds(start, end, bounds, radius))
                return true;
        }
        return false;
    }

    bool RouteIntersectsBounds(
        AirCombatRuntimeRouteGeometry[] routes,
        Bounds bounds)
    {
        if (routes == null)
            return false;
        for (int routeIndex = 0; routeIndex < routes.Length; routeIndex++)
        {
            AirCombatRuntimeRouteGeometry route = routes[routeIndex];
            if (route.kind == AirCombatRouteKind.EnemyIngress ||
                route.localPoints == null)
                continue;
            for (int pointIndex = 1;
                 pointIndex < route.localPoints.Length;
                 pointIndex++)
            {
                Vector3 start = route.localPoints[pointIndex - 1];
                Vector3 end = route.localPoints[pointIndex];
                float verticalSafety = CitySettings.wingspan * 0.45f + 8f;
                if (Mathf.Min(start.y, end.y) >
                    bounds.max.y + verticalSafety)
                {
                    continue;
                }
                if (AirCombatCityGenerator.FootprintIntersectsCorridor(
                        new Vector2(bounds.center.x, bounds.center.z),
                        new Vector2(bounds.size.x, bounds.size.z),
                        0f,
                        new Vector2(start.x, start.z),
                        new Vector2(end.x, end.z),
                        route.width * 0.5f,
                        out _))
                    return true;
            }
        }
        return false;
    }

    static bool SegmentIntersectsExpandedBounds(
        Vector3 start,
        Vector3 end,
        Bounds bounds,
        float radius)
    {
        Bounds expanded = bounds;
        expanded.Expand(Mathf.Max(0f, radius) * 2f);
        Vector3 direction = end - start;
        float length = direction.magnitude;
        if (length <= 0.001f)
            return expanded.Contains(start);
        Ray ray = new Ray(start, direction / length);
        return expanded.IntersectRay(ray, out float distance) &&
               distance <= length;
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
        // Facility objectives deliberately live near the city edge. They are
        // mission targets, not the origin of the whole horde navigation graph.
        // Keeping the old objective-based centre shifted every 144-node air
        // graph toward the northern facility and made otherwise valid PCG
        // ingresses fail the arena-distance and reachability checks.
        layout.combatCenter = ResolveFormalCombatCenter(city, GroundHeight);
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

        SynchronizePowerPositions(layout, city);
        SynchronizeRetreatPoints(layout, city);
    }

    void SynchronizePowerPositions(
        FinitePlanetDefenseLayoutPlan layout,
        AirCombatCityPlan city)
    {
        if (layout.powerPositions == null ||
            layout.powerPositions.Length == 0)
        {
            return;
        }

        if (city.facilityCores.Count > 0)
        {
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
                    outward = Vector3.forward;
                // facilityCores now denotes a validated empty modular-facility
                // pad.  Keep the formal anchor at its actual centre; the
                // objective builder owns only the vertical placement.
                anchor.position = core;
                anchor.position += Vector3.up * GroundHeight;
                anchor.forward = -outward.normalized;
            }
            return;
        }

        List<Vector3> safePoints = CollectSafeCityVolumeCenters(city, false);
        if (safePoints.Count == 0)
            safePoints.Add(city.objective);
        for (int index = 0; index < layout.powerPositions.Length; index++)
        {
            CombatSemanticAnchor anchor = layout.powerPositions[index];
            if (anchor == null)
                continue;
            Vector3 point = safePoints[index % safePoints.Count];
            anchor.position = point + Vector3.up * GroundHeight;
            anchor.forward = HorizontalDirection(point, city.objective);
        }
    }

    public static Vector3 ResolveFormalCombatCenter(
        AirCombatCityPlan city,
        float groundHeight)
    {
        if (city != null)
        {
            for (int index = 0; index < city.volumes.Count; index++)
            {
                AirCombatTacticalVolume volume = city.volumes[index];
                if (volume != null &&
                    volume.kind == AirCombatVolumeKind.ManeuverBowl)
                {
                    return volume.center + Vector3.up * groundHeight;
                }
            }
        }

        // Older serialized plans may not contain the semantic volume. Their
        // geometry is still centred on the local origin, so fail safely to the
        // city centre instead of falling back to an edge objective.
        return new Vector3(0f, groundHeight, 0f);
    }

    void SynchronizeRetreatPoints(
        FinitePlanetDefenseLayoutPlan layout,
        AirCombatCityPlan city)
    {
        if (layout.retreatPoints == null ||
            layout.retreatPoints.Length == 0)
        {
            return;
        }

        // Prefer recovery pockets, but never put an extraction point directly
        // on top of a scan/core objective. If all recovery pockets are already
        // occupied, the other generator-validated city volumes are safer than
        // falling back to a hidden terrain anchor.
        List<Vector3> safePoints =
            CollectSafeCityRetreatCandidates(city);

        var occupied = new List<Vector3>();
        if (layout.powerPositions != null)
        {
            for (int index = 0; index < layout.powerPositions.Length; index++)
            {
                CombatSemanticAnchor objective = layout.powerPositions[index];
                if (objective != null)
                    occupied.Add(objective.position);
            }
        }
        for (int index = 0; index < layout.retreatPoints.Length; index++)
        {
            CombatSemanticAnchor anchor = layout.retreatPoints[index];
            if (anchor == null)
                continue;
            Vector3 point = SelectMostSeparatedPoint(safePoints, occupied);
            anchor.position = point + Vector3.up * GroundHeight;
            anchor.forward = HorizontalDirection(point, city.objective);
            occupied.Add(anchor.position);
        }
    }

    static Vector3 SelectMostSeparatedPoint(
        List<Vector3> candidates,
        List<Vector3> occupied)
    {
        if (candidates == null || candidates.Count == 0)
            return Vector3.zero;
        if (occupied == null || occupied.Count == 0)
            return candidates[0];

        int bestIndex = 0;
        float bestDistanceSquared = -1f;
        for (int candidateIndex = 0;
             candidateIndex < candidates.Count;
             candidateIndex++)
        {
            Vector3 candidate = candidates[candidateIndex];
            float nearestDistanceSquared = float.PositiveInfinity;
            for (int occupiedIndex = 0;
                 occupiedIndex < occupied.Count;
                 occupiedIndex++)
            {
                Vector3 delta = candidate - occupied[occupiedIndex];
                delta.y = 0f;
                nearestDistanceSquared = Mathf.Min(
                    nearestDistanceSquared,
                    delta.sqrMagnitude);
            }
            if (nearestDistanceSquared <= bestDistanceSquared)
                continue;
            bestDistanceSquared = nearestDistanceSquared;
            bestIndex = candidateIndex;
        }
        return candidates[bestIndex];
    }

    static void AppendUniquePoint(List<Vector3> destination, Vector3 point)
    {
        for (int index = 0; index < destination.Count; index++)
        {
            Vector3 delta = destination[index] - point;
            delta.y = 0f;
            if (delta.sqrMagnitude < 1f)
                return;
        }
        destination.Add(point);
    }

    static List<Vector3> CollectSafeCityVolumeCenters(
        AirCombatCityPlan city,
        bool recoveryOnly)
    {
        var result = new List<Vector3>(city.volumes.Count);
        for (int pass = 0; pass < (recoveryOnly ? 1 : 3); pass++)
        {
            for (int index = 0; index < city.volumes.Count; index++)
            {
                AirCombatTacticalVolume volume = city.volumes[index];
                bool accepted = pass == 0
                    ? volume.kind == AirCombatVolumeKind.RecoveryPocket
                    : pass == 1
                        ? volume.kind == AirCombatVolumeKind.ManeuverBowl
                        : volume.kind == AirCombatVolumeKind.SpawnBasin;
                if (!accepted)
                    continue;
                result.Add(volume.center);
            }
        }
        return result;
    }

    static List<Vector3> CollectSafeCityRetreatCandidates(
        AirCombatCityPlan city)
    {
        var result = new List<Vector3>(city.volumes.Count * 5 + 1);
        for (int pass = 0; pass < 3; pass++)
        {
            for (int index = 0; index < city.volumes.Count; index++)
            {
                AirCombatTacticalVolume volume = city.volumes[index];
                bool accepted = pass == 0
                    ? volume.kind == AirCombatVolumeKind.RecoveryPocket
                    : pass == 1
                        ? volume.kind == AirCombatVolumeKind.ManeuverBowl
                        : volume.kind == AirCombatVolumeKind.SpawnBasin;
                if (!accepted)
                    continue;

                AppendUniquePoint(result, volume.center);
                // Mission anchors occupy a point, while these PCG volumes are
                // building-free areas. Add interior alternatives so three
                // scan targets cannot consume every valid extraction choice.
                float offsetX = Mathf.Max(0f, volume.size.x * 0.5f - 28f);
                float offsetZ = Mathf.Max(0f, volume.size.z * 0.5f - 28f);
                if (offsetX >= 20f)
                {
                    AppendUniquePoint(
                        result,
                        volume.center + Vector3.right * offsetX);
                    AppendUniquePoint(
                        result,
                        volume.center - Vector3.right * offsetX);
                }
                if (offsetZ >= 20f)
                {
                    AppendUniquePoint(
                        result,
                        volume.center + Vector3.forward * offsetZ);
                    AppendUniquePoint(
                        result,
                        volume.center - Vector3.forward * offsetZ);
                }
            }
        }
        AppendUniquePoint(result, city.playerSpawn);
        return result;
    }

    static Vector3 HorizontalDirection(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.001f
            ? direction.normalized
            : Vector3.forward;
    }

    static void CreateFlatGroundCollision(Transform parent, float groundSize)
    {
        var ground = new GameObject(
            "UrbanCombatGroundCollision_城市平坦地面");
        ground.transform.SetParent(parent, false);
        ground.transform.localPosition = new Vector3(0f, -1.1f, 0f);
        BoxCollider collider = ground.AddComponent<BoxCollider>();
        float safeGroundSize = Mathf.Max(512f, groundSize);
        collider.size = new Vector3(
            safeGroundSize,
            2f,
            safeGroundSize);
    }

    void HandleChunkActivated(Vector2Int coordinate)
    {
        SuppressUnderlyingTerrainChunks();
    }

    void SuppressUnderlyingTerrainChunks()
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
        CancelConfiguration();
        if (streamer != null)
            streamer.ChunkActivated -= HandleChunkActivated;
    }
}
