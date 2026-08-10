using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.ModularAssembly;

/// <summary>
/// Permanent city-trap presentation scene. It keeps the real PCG recovery
/// courtyard, real PCG building prefabs, production environmental fields and
/// real horde aircraft together in two repeatable demonstrations:
/// magnetic capture/escape and tactical gale/building-impact destruction.
/// </summary>
[DisallowMultipleComponent]
public sealed class UrbanEnvironmentalTrapTestHarness :
    MonoBehaviour
{
    const int TestSeed = 7319;
    const float AttractionSeconds = 3.6f;
    const float ReleaseSettleSeconds = 0.45f;
    const float PathOneSpeed = 42f;
    const float PathTwoSpeed = 46f;
    const float PathThreeSpeed = 64f;
    const float WindApproachSpeed = 7f;
    const float WindImpactTimeoutSeconds = 7.5f;
    const float WindRespawnDelaySeconds = 2.2f;

    enum ObservationView
    {
        Automatic,
        MagneticCourtyard,
        TacticalGale
    }

    sealed class EnemyProbe
    {
        public string label;
        public Rigidbody body;
        public HordeEnemyVehicle enemy;
        public HordeEnemyRole role;
        public Vector3 spawnPosition;
        public Quaternion spawnRotation;
        public int expectedWall = -1;
    }

    readonly List<EnemyProbe> wallEnemies = new List<EnemyProbe>(3);
    readonly List<Collider> realBuildingColliders = new List<Collider>(32);

    UrbanEnvironmentalFieldDirector director;
    UrbanEnvironmentalFieldVolume windField;
    UrbanEnvironmentalFieldVolume magneticField;
    EnemyProbe entryEnemy;
    EnemyProbe windEnemy;
    Rigidbody enemyTargetReference;
    Coroutine magneticLoop;
    Coroutine windLoop;
    Coroutine pendingRestart;
    bool lastGuidanceReached;
    float windImpactLocalZ;
    float nextAutomaticViewAt;
    bool automaticViewShowsWind = true;
    ObservationView observationView = ObservationView.Automatic;
    float windForceMultiplier = 1f;
    float magneticForceMultiplier = 1f;
    GUIStyle titleStyle;
    GUIStyle detailStyle;
    string phase = "正在准备磁场循环";
    string lastCycleResult = "磁场尚未完成第一轮";
    string windPhase = "正在准备战术风场循环";
    string windLastCycleResult = "风场尚未完成第一轮";
    int completedCycles;
    int windCompletedCycles;

    public string Phase => phase;
    public string LastCycleResult => lastCycleResult;
    public int CompletedCycles => completedCycles;
    public string WindPhase => windPhase;
    public string WindLastCycleResult => windLastCycleResult;
    public int WindCompletedCycles => windCompletedCycles;
    public UrbanEnvironmentalFieldVolume WindField => windField;
    public UrbanEnvironmentalFieldVolume MagneticField => magneticField;

    void Start()
    {
        try
        {
            // The dedicated test scene must keep its physics loop alive while
            // the user compares the Game view with Inspector/tool settings.
            Application.runInBackground = true;
            BuildScenario();
            magneticLoop = StartCoroutine(RunMagneticLoop());
            windLoop = StartCoroutine(RunWindLoop());
        }
        catch (Exception exception)
        {
            phase = "测试台创建失败";
            lastCycleResult = exception.Message;
            Debug.LogException(exception, this);
            enabled = false;
        }
    }

    void BuildScenario()
    {
        director = GetComponent<UrbanEnvironmentalFieldDirector>();
        if (director == null)
            director = gameObject.AddComponent<UrbanEnvironmentalFieldDirector>();

        var settings = new AirCombatCitySettings
        {
            seed = TestSeed,
            mission = AirCombatCityMission.Clearance
        };
        AirCombatCityReport report;
        AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
            settings,
            out report);
        if (plan == null || report == null || !report.valid)
        {
            throw new InvalidOperationException(
                "Seed 7319 的 PCG 数据无法生成测试庭院。");
        }

        director.Configure(plan, settings);
        SelectFields();

        Vector3 sourceMagnetPosition = magneticField.transform.position;
        Quaternion sourceMagnetRotation = magneticField.transform.rotation;
        BuildRealRecoveryBuildings(
            plan,
            sourceMagnetPosition,
            sourceMagnetRotation);

        windImpactLocalZ = -windField.LocalSize.z * 0.5f + 185f;
        windField.transform.SetPositionAndRotation(
            new Vector3(-240f, 0f, 30f - windImpactLocalZ),
            Quaternion.identity);
        magneticField.transform.SetPositionAndRotation(
            new Vector3(300f, 0f, 0f),
            Quaternion.identity);

        BuildWindImpactBuilding(plan);
        BuildGroundReference();
        BuildEnemies();
        PositionCamera();

        magneticField.SetControlMode(
            UrbanEnvironmentalFieldControlMode.ForcedDisabled);
        windField.SetControlMode(
            UrbanEnvironmentalFieldControlMode.ForcedDisabled);
        windField.SetForceMultiplier(windForceMultiplier);
        magneticField.SetForceMultiplier(magneticForceMultiplier);
        nextAutomaticViewAt = Time.time + 12f;
    }

    void SelectFields()
    {
        for (int index = 0; index < director.Fields.Count; index++)
        {
            UrbanEnvironmentalFieldVolume candidate = director.Fields[index];
            if (candidate.Kind == UrbanEnvironmentalFieldKind.NaturalStreetGale)
            {
                if (windField == null)
                    windField = candidate;
                else
                    candidate.gameObject.SetActive(false);
                continue;
            }

            if (magneticField == null)
                magneticField = candidate;
            else
                candidate.gameObject.SetActive(false);
        }

        if (windField == null || magneticField == null ||
            magneticField.MagneticWalls.Count != 3)
        {
            throw new InvalidOperationException(
                "隔离测试台没有取得一条真实 PCG 风道和三面真实内壁。");
        }
    }

    void BuildRealRecoveryBuildings(
        AirCombatCityPlan plan,
        Vector3 sourceFieldPosition,
        Quaternion sourceFieldRotation)
    {
        GameObject cityTemplate = Resources.Load<GameObject>(
            "PlanetSurface/UrbanCombatCityTemplate");
        AirCombatCityPcgLab catalogSource = cityTemplate != null
            ? cityTemplate.GetComponent<AirCombatCityPcgLab>()
            : null;
        if (catalogSource == null)
        {
            throw new InvalidOperationException(
                "找不到正式城市模板中的建筑目录。");
        }

        MethodInfo resolver = typeof(AirCombatCityPcgLab).GetMethod(
            "ResolveBuildingPrefab",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (resolver == null)
        {
            throw new MissingMethodException(
                "无法调用 PCG 正式建筑选择器。");
        }

        Quaternion inverseFieldRotation = Quaternion.Inverse(
            sourceFieldRotation);
        for (int wallIndex = 0;
             wallIndex < magneticField.MagneticWalls.Count;
             wallIndex++)
        {
            UrbanMagneticWallSurface wall =
                magneticField.MagneticWalls[wallIndex];
            AirCombatBuildingLot lot = FindBuilding(plan, wall.stableId);
            if (lot == null)
            {
                throw new InvalidOperationException(
                    "PCG 庭院缺少真实楼房地块：" + wall.stableId);
            }

            GameObject prefab = resolver.Invoke(
                catalogSource,
                new object[] { lot }) as GameObject;
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "PCG 没有为庭院楼房选出正式模型：" + lot.stableId);
            }

            GameObject building = Instantiate(
                prefab,
                magneticField.transform,
                false);
            building.name = "真实PCG庭院楼房_" + wallIndex + "_" +
                            lot.stableId;
            NormalizedBuildingModelInfo modelInfo =
                building.GetComponent<NormalizedBuildingModelInfo>();
            Vector3 authoredSize = modelInfo != null
                ? modelInfo.AuthoredSize
                : Vector3.one;
            building.transform.localScale = new Vector3(
                lot.size.x / Mathf.Max(0.1f, authoredSize.x),
                lot.size.y / Mathf.Max(0.1f, authoredSize.y),
                lot.size.z / Mathf.Max(0.1f, authoredSize.z));
            Vector3 buildingPosition = new Vector3(
                lot.center.x,
                0f,
                lot.center.z);
            building.transform.localPosition = inverseFieldRotation *
                (buildingPosition - sourceFieldPosition);
            building.transform.localRotation = inverseFieldRotation *
                Quaternion.Euler(0f, lot.yaw, 0f);

            Collider[] colliders = building.GetComponentsInChildren<Collider>();
            for (int colliderIndex = 0;
                 colliderIndex < colliders.Length;
                 colliderIndex++)
            {
                if (colliders[colliderIndex] == null ||
                    colliders[colliderIndex].isTrigger)
                {
                    continue;
                }
                realBuildingColliders.Add(colliders[colliderIndex]);
            }
        }

        if (realBuildingColliders.Count == 0)
        {
            throw new InvalidOperationException(
                "三栋正式 PCG 楼房没有可用于测试的真实碰撞体。");
        }
    }

    void BuildWindImpactBuilding(AirCombatCityPlan plan)
    {
        GameObject cityTemplate = Resources.Load<GameObject>(
            "PlanetSurface/UrbanCombatCityTemplate");
        AirCombatCityPcgLab catalogSource = cityTemplate != null
            ? cityTemplate.GetComponent<AirCombatCityPcgLab>()
            : null;
        MethodInfo resolver = typeof(AirCombatCityPcgLab).GetMethod(
            "ResolveBuildingPrefab",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (catalogSource == null || resolver == null)
        {
            throw new InvalidOperationException(
                "风场测试无法取得正式 PCG 楼房目录。");
        }

        AirCombatBuildingLot selectedLot = null;
        GameObject selectedPrefab = null;
        float selectedScore = float.NegativeInfinity;
        for (int index = 0; index < plan.buildings.Count; index++)
        {
            AirCombatBuildingLot lot = plan.buildings[index];
            if (lot == null || lot.stableId.StartsWith(
                    "building.recovery.",
                    StringComparison.Ordinal))
            {
                continue;
            }

            float crossWidth = Mathf.Max(lot.size.x, lot.size.z);
            if (crossWidth < windField.LocalSize.x + 18f ||
                lot.size.y < 70f)
            {
                continue;
            }

            GameObject prefab = resolver.Invoke(
                catalogSource,
                new object[] { lot }) as GameObject;
            if (prefab == null ||
                prefab.GetComponentsInChildren<Collider>(true).Length == 0)
            {
                continue;
            }

            // The side-entry aircraft keeps real lateral inertia after the
            // gale bends it. Prefer the widest real PCG facade, not merely the
            // tallest tower, so the diagonal impact remains physically earned.
            float score = crossWidth * 10f + lot.size.y * 0.05f;
            if (score <= selectedScore)
                continue;
            selectedScore = score;
            selectedLot = lot;
            selectedPrefab = prefab;
        }

        if (selectedLot == null || selectedPrefab == null)
        {
            throw new InvalidOperationException(
                "PCG 计划中没有可横跨战术风道的真实碰撞楼房。");
        }

        GameObject building = Instantiate(
            selectedPrefab,
            windField.transform,
            false);
        building.name = "真实PCG风场撞击楼房_" + selectedLot.stableId;
        NormalizedBuildingModelInfo modelInfo =
            building.GetComponent<NormalizedBuildingModelInfo>();
        Vector3 authoredSize = modelInfo != null
            ? modelInfo.AuthoredSize
            : Vector3.one;
        building.transform.localScale = new Vector3(
            selectedLot.size.x / Mathf.Max(0.1f, authoredSize.x),
            selectedLot.size.y / Mathf.Max(0.1f, authoredSize.y),
            selectedLot.size.z / Mathf.Max(0.1f, authoredSize.z));
        bool rotateBroadSide = selectedLot.size.z > selectedLot.size.x;
        building.transform.localRotation = rotateBroadSide
            ? Quaternion.Euler(0f, 90f, 0f)
            : Quaternion.identity;
        building.transform.localPosition = new Vector3(
            0f,
            0f,
            windImpactLocalZ + 30f);
        Physics.SyncTransforms();

        Collider[] colliders = building.GetComponentsInChildren<Collider>();
        Bounds buildingBounds;
        if (!TryResolveBounds(colliders, out buildingBounds))
        {
            throw new InvalidOperationException(
                "选中的真实 PCG 风场楼房没有启用中的碰撞体。");
        }

        Vector3 localMin = windField.transform.InverseTransformPoint(
            buildingBounds.min);
        Vector3 localCenter = windField.transform.InverseTransformPoint(
            buildingBounds.center);
        // The aircraft enters from +X and reaches the impact plane near the
        // street centre after the gale bends it. Centre the real facade on
        // that measured crossing line; either +/-42 m leaves a narrow gap
        // that lets the aircraft skim past the building.
        building.transform.localPosition += new Vector3(
            -localCenter.x,
            -localMin.y,
            windImpactLocalZ - localMin.z);
        Physics.SyncTransforms();

        colliders = building.GetComponentsInChildren<Collider>();
        for (int index = 0; index < colliders.Length; index++)
        {
            Collider collider = colliders[index];
            if (collider != null && collider.enabled && !collider.isTrigger)
                realBuildingColliders.Add(collider);
        }
    }

    static bool TryResolveBounds(
        Collider[] colliders,
        out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        if (colliders == null)
            return false;
        for (int index = 0; index < colliders.Length; index++)
        {
            Collider collider = colliders[index];
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;
            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }
        return hasBounds;
    }

    static AirCombatBuildingLot FindBuilding(
        AirCombatCityPlan plan,
        string stableId)
    {
        for (int index = 0; index < plan.buildings.Count; index++)
        {
            AirCombatBuildingLot building = plan.buildings[index];
            if (building != null && building.stableId == stableId)
                return building;
        }
        return null;
    }

    void BuildGroundReference()
    {
        GameObject windRoad = GameObject.CreatePrimitive(
            PrimitiveType.Cube);
        windRoad.name = "真实长度风道地面_风从道路起点吹向终点";
        windRoad.transform.SetParent(windField.transform, false);
        windRoad.transform.localPosition = new Vector3(0f, -1.5f, 0f);
        windRoad.transform.localScale = new Vector3(
            windField.LocalSize.x + 8f,
            3f,
            windField.LocalSize.z);
        Renderer windRoadRenderer = windRoad.GetComponent<Renderer>();
        if (windRoadRenderer != null)
            windRoadRenderer.material.color = new Color(0.055f, 0.065f, 0.075f);

        GameObject courtyardFloor = GameObject.CreatePrimitive(
            PrimitiveType.Cube);
        courtyardFloor.name = "庭院地面_仅作空间参照";
        courtyardFloor.transform.SetParent(magneticField.transform, false);
        courtyardFloor.transform.localPosition = new Vector3(0f, -1.5f, 0f);
        courtyardFloor.transform.localScale = new Vector3(
            magneticField.LocalSize.x,
            3f,
            magneticField.LocalSize.z);
        Renderer courtyardRenderer = courtyardFloor.GetComponent<Renderer>();
        if (courtyardRenderer != null)
            courtyardRenderer.material.color = new Color(0.07f, 0.09f, 0.12f);
    }

    void BuildEnemies()
    {
        Vector3 entryLocal = GetPathOneStartLocal(0);
        entryEnemy = CreateEnemy(
            "真实小兵_路径1_2_3循环展示机",
            HordeEnemyRole.Striker,
            magneticField.transform.TransformPoint(entryLocal),
            Quaternion.LookRotation(-magneticField.transform.forward, Vector3.up),
            null,
            false);

        GameObject targetReference = new GameObject(
            "风场小兵目标引用_无碰撞不可见");
        enemyTargetReference = targetReference.AddComponent<Rigidbody>();
        enemyTargetReference.useGravity = false;
        enemyTargetReference.isKinematic = true;
        enemyTargetReference.position = windField.transform.TransformPoint(
            new Vector3(0f, 70f, windImpactLocalZ + 80f));

        Vector3 windSpawnLocal = new Vector3(
            windField.LocalSize.x * 0.5f + 20f,
            70f,
            windImpactLocalZ - 36f);
        windEnemy = CreateEnemy(
            "真实小兵_战术风场撞楼循环展示机",
            HordeEnemyRole.Interceptor,
            windField.transform.TransformPoint(windSpawnLocal),
            Quaternion.LookRotation(-windField.transform.right, Vector3.up),
            enemyTargetReference,
            true);
    }

    Vector3 GetPathOneStartLocal(int targetWall)
    {
        Vector3 target = FindUnambiguousWallStart(targetWall);
        target.y = Mathf.Clamp(
            magneticField.MagneticWalls[targetWall].localCenter.y,
            32f,
            magneticField.LocalSize.y - 14f);
        return new Vector3(
            target.x,
            target.y,
            magneticField.LocalSize.z * 0.5f + 82f);
    }

    Vector3 GetPathOneTargetLocal(int targetWall)
    {
        Vector3 target = FindUnambiguousWallStart(targetWall);
        target.y = Mathf.Clamp(
            magneticField.MagneticWalls[targetWall].localCenter.y,
            32f,
            magneticField.LocalSize.y - 14f);
        return target;
    }

    Vector3 FindUnambiguousWallStart(int expectedWall)
    {
        UrbanMagneticWallSurface wall =
            magneticField.MagneticWalls[expectedWall];
        Vector3 inward = wall.localInwardNormal.normalized;
        // Start as far from the selected facade as possible while that facade
        // remains the unique nearest finite wall.  The real city prefabs have
        // balconies and facade machinery protruding beyond the lot plane; a
        // 14 m start can otherwise contact that real shell after only 1 m and
        // make the attraction almost impossible to see.
        float[] distances = { 30f, 26f, 22f, 18f, 14f, 11f, 8f, 6.5f };
        for (int index = 0; index < distances.Length; index++)
        {
            Vector3 candidate = wall.localCenter + inward * distances[index];
            if (SelectNearestWall(candidate) == expectedWall)
                return candidate;
        }
        throw new InvalidOperationException(
            "无法为内壁 " + expectedWall + " 找到单义的小兵起点。");
    }

    int SelectNearestWall(Vector3 localPosition)
    {
        int selected = 0;
        float selectedScore = float.PositiveInfinity;
        for (int index = 0;
             index < magneticField.MagneticWalls.Count;
             index++)
        {
            UrbanMagneticWallSurface wall =
                magneticField.MagneticWalls[index];
            Vector3 inward = wall.localInwardNormal.normalized;
            Vector3 tangent = Vector3.Cross(Vector3.up, inward).normalized;
            Vector3 relative = localPosition - wall.localCenter;
            float planeDistance = Mathf.Abs(Vector3.Dot(relative, inward));
            float tangentOverflow = Mathf.Max(
                0f,
                Mathf.Abs(Vector3.Dot(relative, tangent)) -
                wall.width * 0.5f);
            float verticalOverflow = Mathf.Max(
                0f,
                Mathf.Abs(relative.y) - wall.height * 0.5f);
            float score = planeDistance * planeDistance +
                          tangentOverflow * tangentOverflow +
                          verticalOverflow * verticalOverflow;
            if (score >= selectedScore)
                continue;
            selected = index;
            selectedScore = score;
        }
        return selected;
    }

    static EnemyProbe CreateEnemy(
        string objectName,
        HordeEnemyRole role,
        Vector3 position,
        Quaternion rotation,
        Rigidbody targetBody,
        bool activateForCombat)
    {
        GameObject root = new GameObject(objectName);
        root.transform.SetPositionAndRotation(position, rotation);
        HordeEnemyVehicle enemy = root.AddComponent<HordeEnemyVehicle>();
        string error;
        if (!enemy.Initialize(null, null, null, out error))
        {
            Destroy(root);
            throw new InvalidOperationException(
                "正式小兵模型初始化失败：" + error);
        }

        Rigidbody body = root.GetComponent<Rigidbody>();
        HordeEnemyProfile profile = HordeEnemyProfile.ForRole(role);
        body.mass = profile.massKg;
        body.position = position;
        body.rotation = rotation;

        MethodInfo selector = typeof(HordeEnemyVehicle).GetMethod(
            "SelectRoleVisual",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (selector != null)
            selector.Invoke(enemy, new object[] { role });

        var probe = new EnemyProbe
        {
            label = objectName,
            body = body,
            enemy = enemy,
            role = role,
            spawnPosition = position,
            spawnRotation = rotation
        };
        if (activateForCombat)
        {
            enemy.Activate(
                profile,
                targetBody,
                position,
                rotation,
                0,
                0,
                "trap-test-wind-enemy",
                0);
            SetEnemyDesiredMotion(
                enemy,
                rotation * Vector3.forward * WindApproachSpeed,
                rotation * Vector3.forward);
            body.velocity = rotation * Vector3.forward * WindApproachSpeed;
        }
        return probe;
    }

    IEnumerator RunMagneticLoop()
    {
        int targetWall = 0;
        ResetEnemyAt(
            entryEnemy,
            magneticField.transform.TransformPoint(
                GetPathOneStartLocal(targetWall)),
            Quaternion.LookRotation(-magneticField.transform.forward, Vector3.up));

        while (enabled)
        {
            magneticField.SetControlMode(
                UrbanEnvironmentalFieldControlMode.ForcedDisabled);
            entryEnemy.expectedWall = targetWall;

            phase = "路径 1：从唯一开放面飞入庭院（磁场关闭）";
            Vector3 attractionStart = magneticField.transform.TransformPoint(
                GetPathOneTargetLocal(targetWall));
            yield return GuideEnemyTo(
                entryEnemy,
                attractionStart,
                PathOneSpeed,
                1.5f,
                6.5f);
            bool pathOneReached = lastGuidanceReached;
            entryEnemy.body.velocity = Vector3.zero;
            entryEnemy.body.angularVelocity = Vector3.zero;

            phase = "路径 1 结束：磁场开启，真实吸力接管并贴向内壁 " +
                    (targetWall + 1);
            magneticField.SetControlMode(
                UrbanEnvironmentalFieldControlMode.ForcedActive);
            yield return new WaitForSeconds(AttractionSeconds);

            int assignedWall = ResolveCapturedWall(entryEnemy.body);
            Vector3 wallInward = magneticField.MagneticWalls[targetWall]
                .localInwardNormal.normalized;
            Vector3 attractionStartLocal =
                magneticField.transform.InverseTransformPoint(attractionStart);
            Vector3 attractedLocal =
                magneticField.transform.InverseTransformPoint(
                    entryEnemy.body.worldCenterOfMass);
            float beforeDistance = Vector3.Dot(
                attractionStartLocal -
                magneticField.MagneticWalls[targetWall].localCenter,
                wallInward);
            float afterDistance = Vector3.Dot(
                attractedLocal -
                magneticField.MagneticWalls[targetWall].localCenter,
                wallInward);
            bool attractionWorked = assignedWall == targetWall &&
                                    beforeDistance - afterDistance > 2f &&
                                    afterDistance > 0f;

            phase = "磁场关闭：解除贴墙，准备执行路径 2";
            magneticField.SetControlMode(
                UrbanEnvironmentalFieldControlMode.ForcedDisabled);
            yield return new WaitForSeconds(ReleaseSettleSeconds);

            phase = "路径 2：沿庭院内部垂直爬升，从开放顶部脱困";
            Vector3 releaseLocal = magneticField.transform.InverseTransformPoint(
                entryEnemy.body.worldCenterOfMass);
            Vector3 topExitLocal = new Vector3(
                releaseLocal.x,
                magneticField.LocalSize.y + 30f,
                releaseLocal.z);
            yield return GuideEnemyTo(
                entryEnemy,
                magneticField.transform.TransformPoint(topExitLocal),
                PathTwoSpeed,
                8f,
                5f);
            bool pathTwoReached = lastGuidanceReached &&
                magneticField.transform.InverseTransformPoint(
                    entryEnemy.body.worldCenterOfMass).y >
                magneticField.LocalSize.y + 10f;

            phase = "路径 3：楼顶外侧绕行，返回开放面入口";
            int nextWall = (targetWall + 1) % 3;
            Vector3 nextStartLocal = GetPathOneStartLocal(nextWall);
            Vector3[] returnRoute =
            {
                new Vector3(
                    magneticField.LocalSize.x * 0.5f + 72f,
                    magneticField.LocalSize.y + 30f,
                    releaseLocal.z),
                new Vector3(
                    magneticField.LocalSize.x * 0.5f + 92f,
                    magneticField.LocalSize.y * 0.62f,
                    magneticField.LocalSize.z * 0.05f),
                new Vector3(
                    magneticField.LocalSize.x * 0.5f + 70f,
                    nextStartLocal.y,
                    magneticField.LocalSize.z * 0.5f + 72f),
                nextStartLocal
            };
            bool pathThreeReached = true;
            for (int routeIndex = 0;
                 routeIndex < returnRoute.Length;
                 routeIndex++)
            {
                yield return GuideEnemyTo(
                    entryEnemy,
                    magneticField.transform.TransformPoint(
                        returnRoute[routeIndex]),
                    PathThreeSpeed,
                    8f,
                    4.5f);
                pathThreeReached &= lastGuidanceReached;
            }

            bool noPenetration = !EnemyPenetratesRealBuilding(entryEnemy.body);
            bool passed = pathOneReached && attractionWorked &&
                          pathTwoReached && pathThreeReached && noPenetration;
            lastCycleResult =
                (passed ? "本轮 1→2→3 通过" : "本轮 1→2→3 未通过") +
                "：路径1进入=" + pathOneReached +
                "，吸附墙" + (targetWall + 1) + "=" + attractionWorked +
                "（实际墙=" + (assignedWall + 1) +
                "，朝墙位移=" +
                (beforeDistance - afterDistance).ToString("0.0") +
                "m，最终墙距=" + afterDistance.ToString("0.0") + "m）" +
                "，路径2顶部脱困=" + pathTwoReached +
                "，路径3返回入口=" + pathThreeReached +
                "，未穿入真实楼房=" + noPenetration;
            completedCycles++;
            targetWall = nextWall;
        }
    }

    IEnumerator RunWindLoop()
    {
        while (enabled)
        {
            windField.SetControlMode(
                UrbanEnvironmentalFieldControlMode.ForcedDisabled);
            ReactivateWindEnemy();
            windPhase = "敌机从道路右侧横向切入（航向与风向垂直）";
            yield return new WaitForSeconds(1.15f);

            Vector3 activationLocal = windField.transform.InverseTransformPoint(
                windEnemy.body.worldCenterOfMass);
            float maximumSpeed = windEnemy.body.velocity.magnitude;
            float sideEntryThreshold = windField.LocalSize.x * 0.5f;
            bool startedOutsideSide =
                Mathf.Abs(activationLocal.x) > sideEntryThreshold;
            bool enteredWindFromSide = false;
            float strongestWindwardVelocity = 0f;
            windPhase = "战术风场开启：横穿风道时被沿街狂风折转";
            windField.SetControlMode(
                UrbanEnvironmentalFieldControlMode.ForcedActive);

            float impactDeadline = Time.time + WindImpactTimeoutSeconds;
            while (windEnemy.enemy != null &&
                   windEnemy.enemy.IsCombatCapable &&
                   Time.time < impactDeadline)
            {
                Vector3 localPosition =
                    windField.transform.InverseTransformPoint(
                        windEnemy.body.worldCenterOfMass);
                enteredWindFromSide |=
                    Mathf.Abs(localPosition.x) <= sideEntryThreshold;
                maximumSpeed = Mathf.Max(
                    maximumSpeed,
                    windEnemy.body.velocity.magnitude);
                strongestWindwardVelocity = Mathf.Max(
                    strongestWindwardVelocity,
                    Vector3.Dot(
                        windEnemy.body.velocity,
                        windField.transform.forward));
                yield return new WaitForFixedUpdate();
            }

            bool destroyedByImpact = windEnemy.enemy != null &&
                                     !windEnemy.enemy.IsCombatCapable;
            bool acceleratedByWind =
                maximumSpeed >= WindApproachSpeed + 12f &&
                strongestWindwardVelocity >= 28f;
            int staticCollisionCount = ResolveStaticCollisionCount(
                windEnemy.enemy);
            bool reachedImpactFacade = destroyedByImpact ||
                                       staticCollisionCount > 0;
            bool validSideEntry = startedOutsideSide && enteredWindFromSide;
            bool passed = validSideEntry && destroyedByImpact &&
                          acceleratedByWind && reachedImpactFacade;
            windLastCycleResult =
                (passed ? "本轮风场撞楼通过" : "本轮风场撞楼未通过") +
                "：垂直侧向进入=" + validSideEntry +
                "，风力折转=" + acceleratedByWind +
                "（最高速度=" + maximumSpeed.ToString("0.0") + "m/s）" +
                "，抵达真实楼房=" + reachedImpactFacade +
                "，正式环境撞击销毁=" + destroyedByImpact;
            windCompletedCycles++;
            windPhase = destroyedByImpact
                ? "敌机已爆炸，风场关闭，等待重生"
                : "本轮未完成撞毁，风场关闭并安全复位";
            windField.SetControlMode(
                UrbanEnvironmentalFieldControlMode.ForcedDisabled);
            if (windCompletedCycles <= 2 || !passed)
                Debug.Log("[城市陷阱测试场] " + windLastCycleResult, this);
            yield return new WaitForSeconds(WindRespawnDelaySeconds);
        }
    }

    void ReactivateWindEnemy()
    {
        if (windEnemy == null || windEnemy.enemy == null ||
            windEnemy.body == null)
        {
            return;
        }

        HordeEnemyProfile profile = HordeEnemyProfile.ForRole(windEnemy.role);
        windEnemy.enemy.Activate(
            profile,
            enemyTargetReference,
            windEnemy.spawnPosition,
            windEnemy.spawnRotation,
            0,
            windCompletedCycles + 1,
            "trap-test-wind-enemy",
            0);
        Vector3 direction = windEnemy.spawnRotation * Vector3.forward;
        SetEnemyDesiredMotion(
            windEnemy.enemy,
            direction * WindApproachSpeed,
            direction);
        windEnemy.body.velocity = direction * WindApproachSpeed;
        windEnemy.body.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }

    static void SetEnemyDesiredMotion(
        HordeEnemyVehicle enemy,
        Vector3 desiredVelocity,
        Vector3 desiredAim)
    {
        if (enemy == null)
            return;
        FieldInfo desiredVelocityField = typeof(HordeEnemyVehicle).GetField(
            "desiredVelocity",
            BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo desiredAimField = typeof(HordeEnemyVehicle).GetField(
            "desiredAim",
            BindingFlags.Instance | BindingFlags.NonPublic);
        desiredVelocityField?.SetValue(enemy, desiredVelocity);
        desiredAimField?.SetValue(enemy, desiredAim);
    }

    static int ResolveStaticCollisionCount(HordeEnemyVehicle enemy)
    {
        if (enemy == null)
            return 0;
        FieldInfo field = typeof(HordeEnemyVehicle).GetField(
            "staticCollisionCount",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return field != null ? (int)field.GetValue(enemy) : 0;
    }

    IEnumerator GuideEnemyTo(
        EnemyProbe probe,
        Vector3 destination,
        float maximumSpeed,
        float arrivalDistance,
        float timeoutSeconds)
    {
        lastGuidanceReached = false;
        float elapsed = 0f;
        while (probe != null && probe.body != null &&
               elapsed < timeoutSeconds)
        {
            Vector3 delta = destination - probe.body.worldCenterOfMass;
            float distance = delta.magnitude;
            if (distance <= arrivalDistance)
            {
                lastGuidanceReached = true;
                break;
            }

            Vector3 desiredVelocity = delta.normalized * Mathf.Min(
                maximumSpeed,
                Mathf.Max(12f, distance * 1.35f));
            probe.body.velocity = Vector3.MoveTowards(
                probe.body.velocity,
                desiredVelocity,
                maximumSpeed * 2.8f * Time.fixedDeltaTime);
            if (desiredVelocity.sqrMagnitude > 0.1f)
            {
                Quaternion desiredRotation = Quaternion.LookRotation(
                    desiredVelocity.normalized,
                    Vector3.up);
                probe.body.MoveRotation(Quaternion.Slerp(
                    probe.body.rotation,
                    desiredRotation,
                    5.5f * Time.fixedDeltaTime));
            }
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        if (probe != null && probe.body != null)
        {
            lastGuidanceReached |= Vector3.Distance(
                probe.body.worldCenterOfMass,
                destination) <= arrivalDistance;
            probe.body.velocity *= 0.25f;
            probe.body.angularVelocity = Vector3.zero;
        }
    }

    static void ResetEnemyAt(
        EnemyProbe probe,
        Vector3 position,
        Quaternion rotation)
    {
        if (probe == null || probe.body == null)
            return;
        probe.body.isKinematic = true;
        probe.body.position = position;
        probe.body.rotation = rotation;
        probe.body.velocity = Vector3.zero;
        probe.body.angularVelocity = Vector3.zero;
        probe.body.isKinematic = false;
        probe.body.WakeUp();
        Physics.SyncTransforms();
    }

    int ResolveCapturedWall(Rigidbody body)
    {
        FieldInfo capturesField = typeof(UrbanEnvironmentalFieldVolume).GetField(
            "captures",
            BindingFlags.Instance | BindingFlags.NonPublic);
        IDictionary captures = capturesField != null
            ? capturesField.GetValue(magneticField) as IDictionary
            : null;
        if (captures == null || !captures.Contains(body))
            return -1;
        object record = captures[body];
        if (record == null)
            return -1;
        FieldInfo wallIndexField = record.GetType().GetField(
            "wallIndex",
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic);
        return wallIndexField != null
            ? (int)wallIndexField.GetValue(record)
            : -1;
    }

    bool AnyEnemyPenetratesRealBuilding()
    {
        for (int index = 0; index < wallEnemies.Count; index++)
        {
            if (EnemyPenetratesRealBuilding(wallEnemies[index].body))
                return true;
        }
        return entryEnemy != null &&
               EnemyPenetratesRealBuilding(entryEnemy.body);
    }

    bool EnemyPenetratesRealBuilding(Rigidbody body)
    {
        if (body == null)
            return false;
        Collider[] enemyColliders = body.GetComponentsInChildren<Collider>();
        for (int enemyIndex = 0;
             enemyIndex < enemyColliders.Length;
             enemyIndex++)
        for (int buildingIndex = 0;
             buildingIndex < realBuildingColliders.Count;
             buildingIndex++)
        {
            Collider enemyCollider = enemyColliders[enemyIndex];
            Collider buildingCollider = realBuildingColliders[buildingIndex];
            if (enemyCollider == null || buildingCollider == null ||
                !enemyCollider.enabled || enemyCollider.isTrigger ||
                !buildingCollider.enabled || buildingCollider.isTrigger)
            {
                continue;
            }
            Vector3 direction;
            float distance;
            if (Physics.ComputePenetration(
                    enemyCollider,
                    enemyCollider.transform.position,
                    enemyCollider.transform.rotation,
                    buildingCollider,
                    buildingCollider.transform.position,
                    buildingCollider.transform.rotation,
                    out direction,
                    out distance) &&
                distance > 0.04f)
            {
                return true;
            }
        }
        return false;
    }

    void PositionCamera()
    {
        Camera camera = Camera.main;
        if (camera == null)
            return;
        camera.farClipPlane = 5000f;
        ApplyCameraPose(camera, true);
    }

    void LateUpdate()
    {
        Camera camera = Camera.main;
        if (camera == null || magneticField == null || windField == null)
            return;
        if (observationView == ObservationView.Automatic &&
            Time.unscaledTime >= nextAutomaticViewAt)
        {
            automaticViewShowsWind = !automaticViewShowsWind;
            nextAutomaticViewAt = Time.unscaledTime + 12f;
        }
        ApplyCameraPose(camera, false);
    }

    void ApplyCameraPose(Camera camera, bool immediate)
    {
        bool showWind = observationView == ObservationView.TacticalGale ||
                        (observationView == ObservationView.Automatic &&
                         automaticViewShowsWind);
        Vector3 focus;
        Vector3 position;
        if (showWind)
        {
            focus = windField.transform.TransformPoint(
                new Vector3(0f, 66f, windImpactLocalZ - 18f));
            position = windField.transform.TransformPoint(
                new Vector3(82f, 128f, windImpactLocalZ - 190f));
        }
        else
        {
            focus = magneticField.transform.TransformPoint(
                new Vector3(0f, magneticField.LocalSize.y * 0.43f, 0f));
            // The courtyard opens toward local +Z, so this pose looks through
            // the real opening and keeps all three inner facades readable.
            position = focus + new Vector3(18f, 58f, 185f);
        }

        Quaternion rotation = Quaternion.LookRotation(
            focus - position,
            Vector3.up);
        if (immediate)
        {
            camera.transform.SetPositionAndRotation(position, rotation);
            return;
        }

        float blend = 1f - Mathf.Exp(-3.8f * Time.unscaledDeltaTime);
        camera.transform.position = Vector3.Lerp(
            camera.transform.position,
            position,
            blend);
        camera.transform.rotation = Quaternion.Slerp(
            camera.transform.rotation,
            rotation,
            blend);
    }

    [ContextMenu("立即重新开始循环")]
    public void RestartLoop()
    {
        if (!Application.isPlaying || !enabled)
            return;
        if (pendingRestart != null)
            StopCoroutine(pendingRestart);
        pendingRestart = StartCoroutine(RestartBothLoopsSafely());
    }

    IEnumerator RestartBothLoopsSafely()
    {
        if (magneticLoop != null)
            StopCoroutine(magneticLoop);
        if (windLoop != null)
            StopCoroutine(windLoop);
        magneticLoop = null;
        windLoop = null;
        magneticField?.SetControlMode(
            UrbanEnvironmentalFieldControlMode.ForcedDisabled);
        windField?.SetControlMode(
            UrbanEnvironmentalFieldControlMode.ForcedDisabled);
        yield return null;

        completedCycles = 0;
        windCompletedCycles = 0;
        lastCycleResult = "磁场循环已手动重新开始";
        windLastCycleResult = "风场循环已手动重新开始";
        if (magneticField == null ||
            magneticField.MagneticWalls.Count != 3 ||
            windField == null || entryEnemy == null || windEnemy == null ||
            windEnemy.body == null || windEnemy.enemy == null)
        {
            lastCycleResult = "重启失败：测试对象完整性校验未通过";
            windLastCycleResult = lastCycleResult;
            pendingRestart = null;
            yield break;
        }
        magneticLoop = StartCoroutine(RunMagneticLoop());
        windLoop = StartCoroutine(RunWindLoop());
        pendingRestart = null;
    }

    void OnGUI()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.65f, 0.95f, 1f) }
            };
            detailStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
        }

        GUI.Box(new Rect(18f, 18f, 720f, 324f), GUIContent.none);
        GUI.Label(
            new Rect(34f, 28f, 680f, 30f),
            "城市环境陷阱测试场（永久保留）",
            titleStyle);
        GUI.Label(
            new Rect(34f, 61f, 680f, 142f),
            "【三面磁墙 1→2→3】阶段：" + phase + "\n" +
            "磁场循环：" + completedCycles + "；" + lastCycleResult + "\n\n" +
            "【战术风场撞楼】阶段：" + windPhase + "\n" +
            "风场循环：" + windCompletedCycles + "；" +
            windLastCycleResult,
            detailStyle);

        GUI.Label(
            new Rect(34f, 204f, 150f, 24f),
            "风力倍率  ×" + windForceMultiplier.ToString("0.00"),
            detailStyle);
        float nextWindMultiplier = GUI.HorizontalSlider(
            new Rect(184f, 210f, 390f, 20f),
            windForceMultiplier,
            0.25f,
            2.5f);
        GUI.Label(new Rect(584f, 204f, 120f, 24f), "0.25 — 2.50", detailStyle);
        if (Mathf.Abs(nextWindMultiplier - windForceMultiplier) > 0.001f)
        {
            windForceMultiplier = nextWindMultiplier;
            windField?.SetForceMultiplier(windForceMultiplier);
        }

        GUI.Label(
            new Rect(34f, 236f, 150f, 24f),
            "磁力倍率  ×" + magneticForceMultiplier.ToString("0.00"),
            detailStyle);
        float nextMagneticMultiplier = GUI.HorizontalSlider(
            new Rect(184f, 242f, 390f, 20f),
            magneticForceMultiplier,
            0.25f,
            2.5f);
        GUI.Label(new Rect(584f, 236f, 120f, 24f), "0.25 — 2.50", detailStyle);
        if (Mathf.Abs(
                nextMagneticMultiplier - magneticForceMultiplier) > 0.001f)
        {
            magneticForceMultiplier = nextMagneticMultiplier;
            magneticField?.SetForceMultiplier(magneticForceMultiplier);
        }

        if (GUI.Button(new Rect(34f, 280f, 136f, 32f), "自动切换视角"))
        {
            observationView = ObservationView.Automatic;
            automaticViewShowsWind = true;
            nextAutomaticViewAt = Time.unscaledTime + 12f;
            PositionCamera();
        }
        if (GUI.Button(new Rect(178f, 280f, 136f, 32f), "观察磁场庭院"))
        {
            observationView = ObservationView.MagneticCourtyard;
            PositionCamera();
        }
        if (GUI.Button(new Rect(322f, 280f, 136f, 32f), "观察战术风场"))
        {
            observationView = ObservationView.TacticalGale;
            PositionCamera();
        }
        if (GUI.Button(new Rect(466f, 280f, 136f, 32f), "重新开始双循环"))
            RestartLoop();
        if (GUI.Button(new Rect(610f, 280f, 94f, 32f), "倍率归一"))
        {
            windForceMultiplier = 1f;
            magneticForceMultiplier = 1f;
            windField?.SetForceMultiplier(1f);
            magneticField?.SetForceMultiplier(1f);
        }
    }
}
