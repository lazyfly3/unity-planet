using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlanetRiverSystem : MonoBehaviour
{
    static readonly List<PlanetRiverSystem> ActiveSystems = new List<PlanetRiverSystem>();

    [SerializeField] Material waterMaterial;
    [SerializeField, Min(0.01f)] float surfaceOffset = 0.08f;
    [SerializeField, Min(0.05f)] float rerouteDelay = 0.4f;
    [SerializeField, Min(0.25f)] float samplePadding = 1f;
    [SerializeField, Min(0.02f)] float meshRefreshInterval = 0.1f;
    [Header("Runtime Physics Diagnostics")]
    [SerializeField] float totalWaterVolume;
    [SerializeField] float lastMassBalanceError;

    sealed class RuntimeRiver
    {
        public GalaxyRiverPathSaveEntry data;
        public Mesh riverMesh;
        public Mesh lakeMesh;
    }

    readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    VoxelQuadSphereWorld world;
    PlanetRiverSettings settings = new PlanetRiverSettings();
    GalaxyRiverSaveData snapshot;
    float[][] carveDepth;
    Coroutine rerouteRoutine;
    Material runtimeWaterMaterial;
    readonly List<RuntimeRiver> runtimeRivers = new List<RuntimeRiver>();
    float simulationAccumulator;
    float meshRefreshAccumulator;
    float weatherRainfallRate;

    public bool IsEnabled => settings != null && settings.enabled && snapshot != null;
    public int ConfigurationHash => settings != null ? settings.CalculateHash() : 0;
    public GalaxyRiverSaveData Snapshot => snapshot;
    public float TotalWaterVolume => totalWaterVolume;
    public float LastMassBalanceError => lastMassBalanceError;

    public void SetWeatherRainfall(float rainfallRate)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(223, (int)rainfallRate);}
    try
    {
        weatherRainfallRate = Mathf.Max(0f, rainfallRate);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    struct ShallowWaterFlux
    {
        public float mass;
        public float momentum;
    }

    void OnEnable()
    {
        if (!ActiveSystems.Contains(this))
            ActiveSystems.Add(this);
    }

    void OnDisable()
    {
        ActiveSystems.Remove(this);
    }

    void OnDestroy()
    {
        ClearWaterObjects();
    }

    void FixedUpdate()
    {
        if (!IsEnabled)
            return;

        simulationAccumulator += Time.fixedDeltaTime;
        int steps = 0;
        while (steps < settings.maxSubstepsPerFixedUpdate)
        {
            float step = Mathf.Min(settings.simulationStep, CalculateStableTimeStep());
            if (simulationAccumulator < step)
                break;
            SimulateShallowWater(step);
            simulationAccumulator -= step;
            steps++;
        }

        meshRefreshAccumulator += Time.fixedDeltaTime;
        if (meshRefreshAccumulator >= meshRefreshInterval)
        {
            UpdateWaterMeshes();
            meshRefreshAccumulator = 0f;
        }
    }

    public void Configure(
        VoxelQuadSphereWorld targetWorld,
        PlanetRiverSettings riverSettings,
        GalaxyRiverSaveData savedData)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(215);}
    try
    {
        world = targetWorld;
        settings = riverSettings != null ? riverSettings.Clone() : new PlanetRiverSettings();
        settings.ClampValues();
        snapshot = savedData != null && savedData.configurationHash == ConfigurationHash
            ? CloneSnapshot(savedData)
            : null;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void PrepareHydrology()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(216);}
    try
    {
        ClearWaterObjects();
        if (world == null || !settings.enabled || settings.riverCount <= 0)
        {
            snapshot = null;
            carveDepth = null;
            return;
        }

        if (snapshot == null || snapshot.rivers == null || snapshot.rivers.Length == 0)
            snapshot = GenerateRiverNetwork();
        EnsureSimulationState();
        BuildCarveMap();
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void BuildWaterSurface()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(217);}
    try
    {
        ClearWaterObjects();
        if (!IsEnabled || waterMaterial == null)
            return;

        runtimeWaterMaterial = new Material(waterMaterial)
        {
            name = $"RiverWater_{world.Seed}"
        };
        if (runtimeWaterMaterial.HasProperty("_ShallowColor"))
            runtimeWaterMaterial.SetColor("_ShallowColor", settings.shallowColor);
        if (runtimeWaterMaterial.HasProperty("_DeepColor"))
            runtimeWaterMaterial.SetColor("_DeepColor", settings.deepColor);
        if (runtimeWaterMaterial.HasProperty("_FoamStrength"))
            runtimeWaterMaterial.SetFloat("_FoamStrength", settings.foamStrength);

        GameObject root = new GameObject("GeneratedRiverWater");
        root.transform.SetParent(transform, false);
        root.hideFlags = HideFlags.HideInHierarchy;
        foreach (GalaxyRiverPathSaveEntry river in snapshot.rivers)
        {
            if (river?.nodes == null || river.nodes.Length < 2)
                continue;
            runtimeRivers.Add(new RuntimeRiver
            {
                data = river,
                riverMesh = CreateRiverMesh(root.transform, river, runtimeWaterMaterial),
                lakeMesh = CreateLakeMesh(root.transform, river, runtimeWaterMaterial)
            });
        }
        UpdateWaterMeshes();
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public float GetCarveDepth(QuadSphereFace face, int u, int v)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(218, (int)u, (int)v);}
    try
    {
        if (carveDepth == null || u < 0 || v < 0 || u >= world.FaceGridSize || v >= world.FaceGridSize)
            return 0f;
        return carveDepth[(int)face][v * world.FaceGridSize + u];
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void NotifyVoxelChanged(QuadSphereVoxelAddress address)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(219);}
    try
    {
        if (!IsEnabled || address.Depth > Mathf.CeilToInt(settings.maxDepth + 2f))
            return;
        if (rerouteRoutine != null)
            StopCoroutine(rerouteRoutine);
        rerouteRoutine = StartCoroutine(RerouteAfterDelay(address));
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    IEnumerator RerouteAfterDelay(QuadSphereVoxelAddress changed)
    {
        yield return new WaitForSeconds(rerouteDelay);
        Vector3 changedDirection = VoxelQuadSphereMapping.GetRadialDirection(
            changed.Face, changed.U, changed.V, world.FaceGridSize);
        float angularRadius = settings.localRerouteRadius * Mathf.PI / (2f * world.FaceGridSize);

        foreach (GalaxyRiverPathSaveEntry river in snapshot.rivers)
        {
            if (river?.nodes == null)
                continue;
            for (int i = 1; i < river.nodes.Length - 1; i++)
            {
                if (Vector3.Angle(river.nodes[i].direction, changedDirection) * Mathf.Deg2Rad > angularRadius)
                    continue;
                GalaxyRiverNodeSaveEntry node = river.nodes[i];
                node.direction = Vector3.Slerp(node.direction, changedDirection, 0.3f).normalized;
                float terrainRadius = world.GetProceduralSurfaceRadius(node.direction);
                node.waterRadius = Mathf.Min(node.waterRadius, terrainRadius - node.depth * 0.35f);
                river.nodes[i] = node;
            }
            SmoothRiver(river.nodes);
            if (river.bedRadii != null && river.bedRadii.Length == river.nodes.Length
                && river.waterDepths != null && river.waterDepths.Length == river.nodes.Length)
            {
                for (int i = 0; i < river.nodes.Length; i++)
                    river.bedRadii[i] = river.nodes[i].waterRadius
                        - Mathf.Max(settings.minimumWaterDepth, river.waterDepths[i]);
            }
        }

        BuildCarveMap();
        BuildWaterSurface();
        rerouteRoutine = null;
    }

    public static bool TrySampleAny(Vector3 worldPosition, out WaterSample sample)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(220);}
    try
    {
        float bestDistance = float.PositiveInfinity;
        sample = default;
        bool found = false;
        foreach (PlanetRiverSystem system in ActiveSystems)
        {
            if (!system.TrySample(worldPosition, out WaterSample candidate))
                continue;
            float distance = Mathf.Abs(candidate.signedDistance);
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            sample = candidate;
            found = true;
        }
        return found;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public bool TrySample(Vector3 worldPosition, out WaterSample sample)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(221);}
    try
    {
        sample = default;
        if (!IsEnabled)
            return false;
        Vector3 local = world.transform.InverseTransformPoint(worldPosition) - world.GetPlanetCenterLocal();
        float radius = local.magnitude;
        if (radius < 0.001f)
            return false;
        Vector3 direction = local / radius;
        float best = float.PositiveInfinity;
        GalaxyRiverNodeSaveEntry bestNode = default;
        GalaxyRiverPathSaveEntry bestRiver = null;
        int bestNodeIndex = -1;
        bool bestIsLake = false;
        Vector3 bestFlow = Vector3.zero;

        foreach (GalaxyRiverPathSaveEntry river in snapshot.rivers)
        {
            if (river?.nodes == null)
                continue;
            for (int i = 0; i < river.nodes.Length; i++)
            {
                GalaxyRiverNodeSaveEntry node = river.nodes[i];
                float surfaceDistance = Vector3.Angle(direction, node.direction) * Mathf.Deg2Rad * node.waterRadius;
                float allowed = i == river.nodes.Length - 1 ? river.lakeRadius : node.width * 0.5f + samplePadding;
                if (surfaceDistance > allowed || surfaceDistance >= best)
                    continue;
                best = surfaceDistance;
                bestNode = node;
                bestRiver = river;
                bestNodeIndex = i;
                bestIsLake = i == river.nodes.Length - 1;
                Vector3 next = river.nodes[Mathf.Min(i + 1, river.nodes.Length - 1)].direction;
                bestFlow = Vector3.ProjectOnPlane(next - node.direction, node.direction).normalized * node.flowSpeed;
            }
        }

        if (best == float.PositiveInfinity)
            return false;
        float dynamicDepth = bestIsLake
            ? bestRiver.lakeWaterDepth
            : bestNodeIndex >= 0 && bestRiver.waterDepths != null
                && bestNodeIndex < bestRiver.waterDepths.Length
                ? bestRiver.waterDepths[bestNodeIndex]
                : bestNode.depth;
        float surfaceRadius = bestIsLake && bestRiver.bedRadii != null
            ? bestRiver.bedRadii[bestNodeIndex] + bestRiver.lakeWaterDepth
            : bestNode.waterRadius;
        Vector3 localSurface = world.GetPlanetCenterLocal() + direction * (surfaceRadius + surfaceOffset);
        Vector3 normal = world.transform.TransformDirection(direction).normalized;
        sample = new WaterSample
        {
            surfacePoint = world.transform.TransformPoint(localSurface),
            surfaceNormal = normal,
            flowVelocity = world.transform.TransformDirection(bestFlow),
            depth = dynamicDepth,
            signedDistance = radius - surfaceRadius
        };
        return sample.signedDistance <= sample.depth * 0.35f;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    void EnsureSimulationState()
    {
        foreach (GalaxyRiverPathSaveEntry river in snapshot.rivers)
        {
            if (river?.nodes == null || river.nodes.Length == 0)
                continue;

            int count = river.nodes.Length;
            bool valid = river.bedRadii != null && river.bedRadii.Length == count
                && river.waterDepths != null && river.waterDepths.Length == count
                && river.discharges != null && river.discharges.Length == count;
            if (!valid)
            {
                river.bedRadii = new float[count];
                river.waterDepths = new float[count];
                river.discharges = new float[count];
                for (int i = 0; i < count; i++)
                {
                    GalaxyRiverNodeSaveEntry node = river.nodes[i];
                    river.bedRadii[i] = node.waterRadius - node.depth;
                    river.waterDepths[i] = Mathf.Max(settings.minimumWaterDepth, node.depth);
                    river.discharges[i] = node.width * river.waterDepths[i] * node.flowSpeed;
                }
                river.lakeWaterDepth = Mathf.Max(settings.minimumWaterDepth, river.lakeDepth);
            }

            ApplySimulationStateToNodes(river);
        }
        totalWaterVolume = CalculateTotalWaterVolume();
    }

    void SimulateShallowWater(float deltaTime)
    {
        const float gravity = 9.81f;
        float volumeBefore = CalculateTotalWaterVolume();
        float expectedVolumeChange = 0f;

        foreach (GalaxyRiverPathSaveEntry river in snapshot.rivers)
        {
            if (river?.nodes == null || river.nodes.Length < 2 || river.waterDepths == null)
                continue;

            int count = river.nodes.Length;
            var areas = new float[count];
            var nextAreas = new float[count];
            var nextDischarges = new float[count];
            var cellLengths = new float[count];
            var fluxes = new ShallowWaterFlux[count + 1];

            for (int i = 0; i < count; i++)
            {
                float width = Mathf.Max(0.1f, river.nodes[i].width);
                areas[i] = width * Mathf.Max(settings.minimumWaterDepth, river.waterDepths[i]);
                Vector3 previous = river.nodes[Mathf.Max(0, i - 1)].direction;
                Vector3 next = river.nodes[Mathf.Min(count - 1, i + 1)].direction;
                cellLengths[i] = Mathf.Max(0.25f,
                    Vector3.Angle(previous, next) * Mathf.Deg2Rad * world.PlanetRadius * 0.5f);
            }

            float sourceFlowRate = settings.sourceFlowRate + weatherRainfallRate;
            float upstreamWidth = Mathf.Max(0.1f, river.nodes[0].width);
            float upstreamArea = upstreamWidth * Mathf.Max(settings.minimumWaterDepth, river.waterDepths[0]);
            fluxes[0] = CalculateFlux(
                upstreamArea, sourceFlowRate, upstreamWidth,
                areas[0], river.discharges[0], upstreamWidth, gravity);

            for (int edge = 1; edge < count; edge++)
            {
                fluxes[edge] = CalculateFlux(
                    areas[edge - 1], river.discharges[edge - 1], river.nodes[edge - 1].width,
                    areas[edge], river.discharges[edge], river.nodes[edge].width, gravity);
            }

            float downstreamWidth = Mathf.Max(0.1f, river.nodes[count - 1].width);
            float lakeSurfaceRadius = river.bedRadii[count - 1]
                + Mathf.Max(settings.minimumWaterDepth, river.lakeWaterDepth);
            float downstreamDepth = Mathf.Max(settings.minimumWaterDepth,
                lakeSurfaceRadius - river.bedRadii[count - 1]);
            float downstreamArea = downstreamWidth * downstreamDepth;
            fluxes[count] = CalculateFlux(
                areas[count - 1], river.discharges[count - 1], downstreamWidth,
                downstreamArea, Mathf.Max(0f, river.discharges[count - 1]), downstreamWidth, gravity);

            for (int i = 0; i < count; i++)
            {
                float dx = cellLengths[i];
                float area = areas[i] - deltaTime / dx * (fluxes[i + 1].mass - fluxes[i].mass);
                float discharge = river.discharges[i]
                    - deltaTime / dx * (fluxes[i + 1].momentum - fluxes[i].momentum);

                float bedSlope;
                if (i == 0)
                    bedSlope = (river.bedRadii[0] - river.bedRadii[1]) / dx;
                else if (i == count - 1)
                    bedSlope = (river.bedRadii[count - 2] - river.bedRadii[count - 1]) / dx;
                else
                    bedSlope = (river.bedRadii[i - 1] - river.bedRadii[i + 1]) / (2f * dx);
                discharge += gravity * areas[i] * bedSlope * deltaTime;

                float width = Mathf.Max(0.1f, river.nodes[i].width);
                float depth = Mathf.Max(settings.minimumWaterDepth, area / width);
                float wettedPerimeter = width + 2f * depth;
                float hydraulicRadius = Mathf.Max(0.01f, area / wettedPerimeter);
                float friction = gravity * settings.manningRoughness * settings.manningRoughness
                    * discharge * Mathf.Abs(discharge)
                    / Mathf.Max(0.0001f, area * Mathf.Pow(hydraulicRadius, 4f / 3f));
                discharge -= friction * deltaTime;

                float minimumArea = width * settings.minimumWaterDepth;
                nextAreas[i] = Mathf.Max(minimumArea, area);
                nextDischarges[i] = Mathf.Clamp(discharge,
                    -nextAreas[i] * 12f, nextAreas[i] * 12f);
            }

            for (int i = 0; i < count; i++)
            {
                float width = Mathf.Max(0.1f, river.nodes[i].width);
                river.waterDepths[i] = nextAreas[i] / width;
                river.discharges[i] = nextDischarges[i];
            }

            float lakeArea = Mathf.PI * river.lakeRadius * river.lakeRadius;
            float directLakeRainfall = weatherRainfallRate * 0.35f;
            float lakeInflow = fluxes[count].mass + directLakeRainfall;
            float lakeOutflow = Mathf.Min(sourceFlowRate,
                Mathf.Sqrt(Mathf.Max(0f, 2f * gravity * river.lakeWaterDepth)) * lakeArea * 0.01f);
            float evaporation = settings.evaporationRate * lakeArea;
            river.lakeWaterDepth = Mathf.Max(settings.minimumWaterDepth,
                river.lakeWaterDepth + (lakeInflow - lakeOutflow - evaporation) * deltaTime / lakeArea);
            expectedVolumeChange += (fluxes[0].mass + directLakeRainfall - lakeOutflow - evaporation) * deltaTime;

            ApplySimulationStateToNodes(river);
        }

        totalWaterVolume = CalculateTotalWaterVolume();
        lastMassBalanceError = totalWaterVolume - volumeBefore - expectedVolumeChange;
    }

    float CalculateStableTimeStep()
    {
        const float gravity = 9.81f;
        float stableStep = settings.simulationStep;
        foreach (GalaxyRiverPathSaveEntry river in snapshot.rivers)
        {
            if (river?.nodes == null || river.waterDepths == null || river.discharges == null)
                continue;
            for (int i = 0; i < river.nodes.Length; i++)
            {
                float width = Mathf.Max(0.1f, river.nodes[i].width);
                float depth = Mathf.Max(settings.minimumWaterDepth, river.waterDepths[i]);
                float area = width * depth;
                float velocity = Mathf.Abs(river.discharges[i] / Mathf.Max(0.0001f, area));
                Vector3 previous = river.nodes[Mathf.Max(0, i - 1)].direction;
                Vector3 next = river.nodes[Mathf.Min(river.nodes.Length - 1, i + 1)].direction;
                float cellLength = Mathf.Max(0.25f,
                    Vector3.Angle(previous, next) * Mathf.Deg2Rad * world.PlanetRadius * 0.5f);
                float localStep = 0.45f * cellLength
                    / Mathf.Max(0.01f, velocity + Mathf.Sqrt(gravity * depth));
                stableStep = Mathf.Min(stableStep, localStep);
            }
        }
        return Mathf.Max(0.001f, stableStep);
    }

    static ShallowWaterFlux CalculateFlux(float leftArea, float leftDischarge, float leftWidth,
        float rightArea, float rightDischarge, float rightWidth, float gravity)
    {
        leftArea = Mathf.Max(0.0001f, leftArea);
        rightArea = Mathf.Max(0.0001f, rightArea);
        leftWidth = Mathf.Max(0.1f, leftWidth);
        rightWidth = Mathf.Max(0.1f, rightWidth);
        float leftDepth = leftArea / leftWidth;
        float rightDepth = rightArea / rightWidth;
        float leftVelocity = leftDischarge / leftArea;
        float rightVelocity = rightDischarge / rightArea;
        float waveSpeed = Mathf.Max(
            Mathf.Abs(leftVelocity) + Mathf.Sqrt(gravity * leftDepth),
            Mathf.Abs(rightVelocity) + Mathf.Sqrt(gravity * rightDepth));
        float leftMomentumFlux = leftDischarge * leftVelocity
            + 0.5f * gravity * leftWidth * leftDepth * leftDepth;
        float rightMomentumFlux = rightDischarge * rightVelocity
            + 0.5f * gravity * rightWidth * rightDepth * rightDepth;
        return new ShallowWaterFlux
        {
            mass = 0.5f * (leftDischarge + rightDischarge)
                - 0.5f * waveSpeed * (rightArea - leftArea),
            momentum = 0.5f * (leftMomentumFlux + rightMomentumFlux)
                - 0.5f * waveSpeed * (rightDischarge - leftDischarge)
        };
    }

    public static void ApplyImpulseAny(Vector3 worldPosition, Vector3 impulse)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(222);}
    try
    {
        foreach (PlanetRiverSystem system in ActiveSystems)
        {
            if (system.ApplyImpulse(worldPosition, impulse))
                return;
        }
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    bool ApplyImpulse(Vector3 worldPosition, Vector3 impulse)
    {
        if (!IsEnabled || impulse.sqrMagnitude < 0.000001f)
            return false;
        Vector3 local = world.transform.InverseTransformPoint(worldPosition) - world.GetPlanetCenterLocal();
        if (local.sqrMagnitude < 0.0001f)
            return false;
        Vector3 direction = local.normalized;
        float bestDistance = float.PositiveInfinity;
        GalaxyRiverPathSaveEntry bestRiver = null;
        int bestIndex = -1;
        for (int riverIndex = 0; riverIndex < snapshot.rivers.Length; riverIndex++)
        {
            GalaxyRiverPathSaveEntry river = snapshot.rivers[riverIndex];
            if (river?.nodes == null)
                continue;
            for (int i = 0; i < river.nodes.Length; i++)
            {
                GalaxyRiverNodeSaveEntry node = river.nodes[i];
                float distance = Vector3.Angle(direction, node.direction) * Mathf.Deg2Rad * node.waterRadius;
                float allowed = i == river.nodes.Length - 1 ? river.lakeRadius : node.width;
                if (distance > allowed || distance >= bestDistance)
                    continue;
                bestDistance = distance;
                bestRiver = river;
                bestIndex = i;
            }
        }
        if (bestRiver == null || bestIndex < 0)
            return false;

        Vector3 previous = bestRiver.nodes[Mathf.Max(0, bestIndex - 1)].direction;
        Vector3 next = bestRiver.nodes[Mathf.Min(bestRiver.nodes.Length - 1, bestIndex + 1)].direction;
        Vector3 localTangent = Vector3.ProjectOnPlane(next - previous, direction).normalized;
        Vector3 worldTangent = world.transform.TransformDirection(localTangent).normalized;
        float cellLength = Mathf.Max(0.25f,
            Vector3.Angle(previous, next) * Mathf.Deg2Rad * world.PlanetRadius * 0.5f);
        const float waterDensity = 1000f;
        bestRiver.discharges[bestIndex] += Vector3.Dot(impulse, worldTangent)
            / (waterDensity * cellLength);
        return true;
    }

    void ApplySimulationStateToNodes(GalaxyRiverPathSaveEntry river)
    {
        for (int i = 0; i < river.nodes.Length; i++)
        {
            GalaxyRiverNodeSaveEntry node = river.nodes[i];
            float depth = Mathf.Max(settings.minimumWaterDepth, river.waterDepths[i]);
            float area = Mathf.Max(0.0001f, node.width * depth);
            node.waterRadius = river.bedRadii[i] + depth;
            node.depth = depth;
            node.flowSpeed = river.discharges[i] / area;
            river.nodes[i] = node;
        }
    }

    float CalculateTotalWaterVolume()
    {
        if (snapshot?.rivers == null)
            return 0f;
        float volume = 0f;
        foreach (GalaxyRiverPathSaveEntry river in snapshot.rivers)
        {
            if (river?.nodes == null || river.waterDepths == null)
                continue;
            for (int i = 0; i < river.nodes.Length; i++)
            {
                Vector3 previous = river.nodes[Mathf.Max(0, i - 1)].direction;
                Vector3 next = river.nodes[Mathf.Min(river.nodes.Length - 1, i + 1)].direction;
                float length = Mathf.Max(0.25f,
                    Vector3.Angle(previous, next) * Mathf.Deg2Rad * world.PlanetRadius * 0.5f);
                volume += river.nodes[i].width * river.waterDepths[i] * length;
            }
            volume += Mathf.PI * river.lakeRadius * river.lakeRadius * river.lakeWaterDepth;
        }
        return volume;
    }

    GalaxyRiverSaveData GenerateRiverNetwork()
    {
        var random = new System.Random(world.Seed + settings.seedOffset);
        var rivers = new GalaxyRiverPathSaveEntry[settings.riverCount];
        for (int riverIndex = 0; riverIndex < rivers.Length; riverIndex++)
        {
            Vector3 source = FindExtremeDirection(random, true);
            Vector3 sink = FindExtremeDirection(random, false, source);
            var nodes = new GalaxyRiverNodeSaveEntry[settings.nodesPerRiver];
            float previousRadius = float.PositiveInfinity;
            for (int i = 0; i < nodes.Length; i++)
            {
                float t = i / (nodes.Length - 1f);
                Vector3 direction = Vector3.Slerp(source, sink, t).normalized;
                if (i > 0 && i < nodes.Length - 1)
                {
                    Vector3 tangent = Vector3.Cross(direction, sink - source).normalized;
                    float meander = Mathf.Sin((t * 5f + riverIndex) * Mathf.PI) * 0.035f;
                    direction = (direction + tangent * meander).normalized;
                }
                float depth = Mathf.Lerp(settings.minDepth, settings.maxDepth, t);
                float terrainRadius = world.GetProceduralSurfaceRadius(direction);
                float waterRadius = Mathf.Min(terrainRadius - depth * 0.3f, previousRadius - 0.015f);
                previousRadius = waterRadius;
                nodes[i] = new GalaxyRiverNodeSaveEntry
                {
                    direction = direction,
                    waterRadius = waterRadius,
                    width = Mathf.Lerp(settings.minWidth, settings.maxWidth, t),
                    depth = depth,
                    flowSpeed = settings.flowSpeed
                };
            }
            rivers[riverIndex] = new GalaxyRiverPathSaveEntry
            {
                nodes = nodes,
                lakeRadius = settings.lakeRadius,
                lakeDepth = settings.lakeDepth
            };
        }
        return new GalaxyRiverSaveData { configurationHash = ConfigurationHash, rivers = rivers };
    }

    Vector3 FindExtremeDirection(System.Random random, bool highest, Vector3 avoid = default)
    {
        Vector3 best = RandomDirection(random);
        float bestHeight = world.GetProceduralSurfaceRadius(best);
        for (int i = 0; i < 96; i++)
        {
            Vector3 candidate = RandomDirection(random);
            if (avoid.sqrMagnitude > 0f && Vector3.Angle(avoid, candidate) < 45f)
                continue;
            float height = world.GetProceduralSurfaceRadius(candidate);
            if ((highest && height > bestHeight) || (!highest && height < bestHeight))
            {
                best = candidate;
                bestHeight = height;
            }
        }
        return best;
    }

    static Vector3 RandomDirection(System.Random random)
    {
        float y = (float)(random.NextDouble() * 2.0 - 1.0);
        float angle = (float)(random.NextDouble() * Mathf.PI * 2.0);
        float horizontal = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
        return new Vector3(horizontal * Mathf.Cos(angle), y, horizontal * Mathf.Sin(angle));
    }

    void BuildCarveMap()
    {
        int size = world.FaceGridSize;
        carveDepth = new float[6][];
        for (int face = 0; face < 6; face++)
        {
            carveDepth[face] = new float[size * size];
            for (int v = 0; v < size; v++)
            for (int u = 0; u < size; u++)
            {
                Vector3 direction = VoxelQuadSphereMapping.GetRadialDirection((QuadSphereFace)face, u, v, size);
                float bestCarve = 0f;
                foreach (GalaxyRiverPathSaveEntry river in snapshot.rivers)
                {
                    if (river?.nodes == null)
                        continue;
                    for (int i = 0; i < river.nodes.Length; i++)
                    {
                        GalaxyRiverNodeSaveEntry node = river.nodes[i];
                        float distance = Vector3.Angle(direction, node.direction) * Mathf.Deg2Rad * world.PlanetRadius;
                        float width = i == river.nodes.Length - 1 ? river.lakeRadius : node.width * 0.5f;
                        if (distance >= width)
                            continue;
                        float profile = 1f - Mathf.SmoothStep(0f, 1f, distance / width);
                        float waterDepth = i == river.nodes.Length - 1 ? river.lakeDepth : node.depth;
                        float terrainRadius = world.GetProceduralSurfaceRadius(direction);
                        float targetBedRadius = river.bedRadii != null && i < river.bedRadii.Length
                            ? river.bedRadii[i]
                            : node.waterRadius - waterDepth;
                        float requiredCarve = Mathf.Max(waterDepth, terrainRadius - targetBedRadius);
                        bestCarve = Mathf.Max(bestCarve, requiredCarve * profile);
                    }
                }
                carveDepth[face][v * size + u] = bestCarve;
            }
        }
    }

    Mesh CreateRiverMesh(Transform parent, GalaxyRiverPathSaveEntry river, Material material)
    {
        var vertices = new Vector3[river.nodes.Length * 2];
        var normals = new Vector3[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var colors = new Color[vertices.Length];
        var triangles = new int[(river.nodes.Length - 1) * 6];
        float distanceUv = 0f;
        for (int i = 0; i < river.nodes.Length; i++)
        {
            GalaxyRiverNodeSaveEntry node = river.nodes[i];
            Vector3 next = river.nodes[Mathf.Min(i + 1, river.nodes.Length - 1)].direction;
            Vector3 previous = river.nodes[Mathf.Max(0, i - 1)].direction;
            Vector3 tangent = Vector3.ProjectOnPlane(next - previous, node.direction).normalized;
            Vector3 right = Vector3.Cross(node.direction, tangent).normalized;
            Vector3 center = world.GetPlanetCenterLocal() + node.direction * (node.waterRadius + surfaceOffset);
            vertices[i * 2] = center - right * node.width * 0.5f;
            vertices[i * 2 + 1] = center + right * node.width * 0.5f;
            normals[i * 2] = normals[i * 2 + 1] = node.direction;
            if (i > 0)
                distanceUv += Vector3.Distance(vertices[i * 2], vertices[(i - 1) * 2]);
            uv[i * 2] = new Vector2(0f, distanceUv * 0.2f);
            uv[i * 2 + 1] = new Vector2(1f, distanceUv * 0.2f);
            colors[i * 2] = colors[i * 2 + 1] = new Color(node.flowSpeed, node.depth, 0f, 1f);
            if (i == river.nodes.Length - 1)
                continue;
            int t = i * 6;
            int v = i * 2;
            triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }
        return CreateMeshObject("River", parent, vertices, normals, uv, colors, triangles, material);
    }

    Mesh CreateLakeMesh(Transform parent, GalaxyRiverPathSaveEntry river, Material material)
    {
        const int segments = 32;
        GalaxyRiverNodeSaveEntry node = river.nodes[river.nodes.Length - 1];
        var vertices = new Vector3[segments + 1];
        var normals = new Vector3[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var colors = new Color[vertices.Length];
        var triangles = new int[segments * 3];
        Vector3 reference = Mathf.Abs(Vector3.Dot(node.direction, Vector3.up)) < 0.9f
            ? Vector3.up
            : Vector3.right;
        Vector3 right = Vector3.Cross(reference, node.direction).normalized;
        Vector3 forward = Vector3.Cross(node.direction, right).normalized;
        Vector3 center = world.GetPlanetCenterLocal() + node.direction * (node.waterRadius + surfaceOffset);
        vertices[0] = center; normals[0] = node.direction; uv[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 offset = (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * river.lakeRadius;
            Vector3 direction = (node.direction * node.waterRadius + offset).normalized;
            vertices[i + 1] = world.GetPlanetCenterLocal() + direction * (node.waterRadius + surfaceOffset);
            normals[i + 1] = direction;
            uv[i + 1] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.5f + Vector2.one * 0.5f;
            colors[i + 1] = new Color(0f, river.lakeDepth, 0f, 1f);
            int next = (i + 1) % segments;
            triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = next + 1;
        }
        return CreateMeshObject("Lake", parent, vertices, normals, uv, colors, triangles, material);
    }

    Mesh CreateMeshObject(string objectName, Transform parent, Vector3[] vertices, Vector3[] normals,
        Vector2[] uv, Color[] colors, int[] triangles, Material material)
    {
        Mesh mesh = new Mesh { name = objectName + "Mesh" };
        mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.colors = colors;
        mesh.triangles = triangles; mesh.RecalculateBounds();
        runtimeMeshes.Add(mesh);
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return mesh;
    }

    void UpdateWaterMeshes()
    {
        foreach (RuntimeRiver runtime in runtimeRivers)
        {
            if (runtime?.data?.nodes == null)
                continue;
            UpdateRiverMesh(runtime.riverMesh, runtime.data);
            UpdateLakeMesh(runtime.lakeMesh, runtime.data);
        }
    }

    void UpdateRiverMesh(Mesh mesh, GalaxyRiverPathSaveEntry river)
    {
        if (mesh == null || mesh.vertexCount != river.nodes.Length * 2)
            return;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Color[] colors = mesh.colors;
        for (int i = 0; i < river.nodes.Length; i++)
        {
            GalaxyRiverNodeSaveEntry node = river.nodes[i];
            Vector3 next = river.nodes[Mathf.Min(i + 1, river.nodes.Length - 1)].direction;
            Vector3 previous = river.nodes[Mathf.Max(0, i - 1)].direction;
            Vector3 tangent = Vector3.ProjectOnPlane(next - previous, node.direction).normalized;
            Vector3 right = Vector3.Cross(node.direction, tangent).normalized;
            Vector3 center = world.GetPlanetCenterLocal() + node.direction * (node.waterRadius + surfaceOffset);
            vertices[i * 2] = center - right * node.width * 0.5f;
            vertices[i * 2 + 1] = center + right * node.width * 0.5f;
            normals[i * 2] = normals[i * 2 + 1] = node.direction;
            colors[i * 2] = colors[i * 2 + 1] = new Color(
                Mathf.Abs(node.flowSpeed), node.depth, Mathf.Sign(node.flowSpeed) * 0.5f + 0.5f, 1f);
        }
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.colors = colors;
        mesh.RecalculateBounds();
    }

    void UpdateLakeMesh(Mesh mesh, GalaxyRiverPathSaveEntry river)
    {
        if (mesh == null || river.nodes.Length == 0)
            return;
        GalaxyRiverNodeSaveEntry node = river.nodes[river.nodes.Length - 1];
        float lakeSurfaceRadius = river.bedRadii[river.bedRadii.Length - 1] + river.lakeWaterDepth;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Color[] colors = mesh.colors;
        Vector3 reference = Mathf.Abs(Vector3.Dot(node.direction, Vector3.up)) < 0.9f
            ? Vector3.up : Vector3.right;
        Vector3 right = Vector3.Cross(reference, node.direction).normalized;
        Vector3 forward = Vector3.Cross(node.direction, right).normalized;
        vertices[0] = world.GetPlanetCenterLocal() + node.direction * (lakeSurfaceRadius + surfaceOffset);
        normals[0] = node.direction;
        colors[0] = new Color(0f, river.lakeWaterDepth, 0f, 1f);
        for (int i = 1; i < vertices.Length; i++)
        {
            float angle = (i - 1) * Mathf.PI * 2f / (vertices.Length - 1);
            Vector3 offset = (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * river.lakeRadius;
            Vector3 direction = (node.direction * lakeSurfaceRadius + offset).normalized;
            vertices[i] = world.GetPlanetCenterLocal() + direction * (lakeSurfaceRadius + surfaceOffset);
            normals[i] = direction;
            colors[i] = new Color(0f, river.lakeWaterDepth, 0f, 1f);
        }
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.colors = colors;
        mesh.RecalculateBounds();
    }

    void ClearWaterObjects()
    {
        Transform oldRoot = transform.Find("GeneratedRiverWater");
        if (oldRoot != null)
            Destroy(oldRoot.gameObject);
        foreach (Mesh mesh in runtimeMeshes)
            if (mesh != null) Destroy(mesh);
        runtimeMeshes.Clear();
        runtimeRivers.Clear();
        if (runtimeWaterMaterial != null)
        {
            Destroy(runtimeWaterMaterial);
            runtimeWaterMaterial = null;
        }
    }

    static void SmoothRiver(GalaxyRiverNodeSaveEntry[] nodes)
    {
        for (int i = 1; i < nodes.Length - 1; i++)
        {
            GalaxyRiverNodeSaveEntry node = nodes[i];
            node.direction = Vector3.Slerp(node.direction,
                Vector3.Slerp(nodes[i - 1].direction, nodes[i + 1].direction, 0.5f), 0.35f).normalized;
            node.waterRadius = Mathf.Min(node.waterRadius,
                Mathf.Lerp(nodes[i - 1].waterRadius, nodes[i + 1].waterRadius, 0.5f));
            nodes[i] = node;
        }
    }

    static GalaxyRiverSaveData CloneSnapshot(GalaxyRiverSaveData source)
    {
        var result = new GalaxyRiverSaveData
        {
            configurationHash = source.configurationHash,
            rivers = new GalaxyRiverPathSaveEntry[source.rivers != null ? source.rivers.Length : 0]
        };
        for (int i = 0; i < result.rivers.Length; i++)
        {
            GalaxyRiverPathSaveEntry river = source.rivers[i];
            result.rivers[i] = new GalaxyRiverPathSaveEntry
            {
                lakeRadius = river.lakeRadius,
                lakeDepth = river.lakeDepth,
                nodes = river.nodes != null ? (GalaxyRiverNodeSaveEntry[])river.nodes.Clone() : new GalaxyRiverNodeSaveEntry[0],
                bedRadii = river.bedRadii != null ? (float[])river.bedRadii.Clone() : null,
                waterDepths = river.waterDepths != null ? (float[])river.waterDepths.Clone() : null,
                discharges = river.discharges != null ? (float[])river.discharges.Clone() : null,
                lakeWaterDepth = river.lakeWaterDepth
            };
        }
        return result;
    }
}
