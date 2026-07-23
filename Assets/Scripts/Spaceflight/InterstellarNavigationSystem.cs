using System;
using System.Collections.Generic;
using UnityEngine;

public struct InterstellarPlanetTargetSnapshot
{
    public string planetId;
    public string displayName;
    public DoubleVector3 universePosition;
    public double distance;
    public Color color;
    public bool locked;
    public bool visited;
}

[DefaultExecutionOrder(-400)]
[DisallowMultipleComponent]
public sealed class InterstellarNavigationSystem : MonoBehaviour
{
    sealed class PlanetTarget
    {
        public GalaxyPlanetDefinition definition;
        public DoubleVector3 universePosition;
        public SpacePlanetProxy proxy;
        public double distance;
        public bool visited;
    }

    [SerializeField] InterstellarFlightRuntime runtime;
    [SerializeField] Transform proxyRoot;
    [SerializeField, Range(4, 12)] int scanRadiusSectors = 8;
    [SerializeField, Range(1, 16)] int maximumVisiblePlanets = 16;
    [SerializeField, Min(100f)] float planetVisualRadius = 280f;
    [SerializeField, Min(1000f)] float nearProxyDistance = 30000f;
    [SerializeField, Min(100f)] float approachDistance = 720f;
    [SerializeField, Min(1f)] float maximumEntrySpeed = 260f;

    readonly List<PlanetTarget> targets = new List<PlanetTarget>(16);
    InterstellarCoordinate lastScanSector;
    int lockedIndex = -1;
    bool scanned;
    Material planetMaterial;
    Material atmosphereMaterial;
    bool transitionStarted;
    bool automaticLandingRequested;

    public bool HasTarget => HasLockedTarget;
    public bool HasLockedTarget => lockedIndex >= 0 && lockedIndex < targets.Count;
    public GalaxyPlanetDefinition SelectedPlanet => LockedPlanet;
    public GalaxyPlanetDefinition LockedPlanet => HasLockedTarget ? targets[lockedIndex].definition : null;
    public DoubleVector3 LockedUniversePosition => HasLockedTarget
        ? targets[lockedIndex].universePosition
        : DoubleVector3.Zero;
    public string TargetName => LockedPlanet == null ? string.Empty : LockedPlanet.displayName;
    public double TargetDistance => HasLockedTarget ? targets[lockedIndex].distance : 0d;
    public int TargetCount => targets.Count;
    public Vector3 DirectionToTarget => GetDirectionToUniversePosition(LockedUniversePosition);
    public bool AutomaticLandingRequested => automaticLandingRequested;
    public double SelectedApproachBoundaryDistance => HasLockedTarget
        ? GetApproachBoundaryDistance(LockedPlanet)
        : 0d;

    public bool CanEnterSelected
    {
        get
        {
            if (!HasLockedTarget || runtime == null)
                return false;
            return TargetDistance <= GetApproachBoundaryDistance(LockedPlanet);
        }
    }

    public bool IsSelectedEntryVelocitySafe => HasLockedTarget
        && runtime != null
        && runtime.ShipBody != null
        && runtime.ShipBody.velocity.magnitude <= CalculateAllowedEntrySpeed(LockedPlanet, TargetDistance);

    void Awake()
    {
        Camera camera = Camera.main;
        if (camera != null)
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, 100000f);
        if (runtime == null)
            runtime = FindObjectOfType<InterstellarFlightRuntime>();
        if (proxyRoot == null)
            proxyRoot = GameObject.Find("PlanetRuntimeRoot")?.transform ?? transform;
        planetMaterial = CreatePlanetMaterial();
        atmosphereMaterial = CreateAtmosphereMaterial();
    }

    void OnEnable()
    {
        if (runtime == null)
            runtime = FindObjectOfType<InterstellarFlightRuntime>();
        if (runtime != null)
        {
            runtime.OriginShifted += HandleOriginShift;
            runtime.UniverseRelocated += HandleUniverseRelocated;
        }
    }

    void Start()
    {
        RefreshTargets(true);
    }

    void Update()
    {
        RefreshTargets(false);
        UpdateTargetDistances();
        if (Input.GetKeyDown(KeyCode.N))
            LockNextTarget();
        if (!transitionStarted && TryFindApproachBoundaryTarget(out int entryIndex))
        {
            SetLockedIndex(entryIndex);
            BeginSelectedPlanetApproach();
        }
    }

    public void RequestAutomaticLanding(bool requested)
    {
        automaticLandingRequested = requested;
    }

    public bool TryLockReticleTarget(Camera camera, float coneDegrees)
    {
        if (camera == null || targets.Count == 0)
            return false;

        int bestIndex = -1;
        float bestAngle = Mathf.Max(0.1f, coneDegrees);
        double bestDistance = double.PositiveInfinity;
        for (int index = 0; index < targets.Count; index++)
        {
            Vector3 direction = GetDirectionToUniversePosition(targets[index].universePosition);
            if (direction.sqrMagnitude < 0.0001f)
                continue;
            float angle = Vector3.Angle(camera.transform.forward, direction);
            if (angle > coneDegrees || angle > bestAngle + 0.001f)
                continue;
            if (angle < bestAngle - 0.001f || targets[index].distance < bestDistance)
            {
                bestIndex = index;
                bestAngle = angle;
                bestDistance = targets[index].distance;
            }
        }
        if (bestIndex < 0)
            return false;
        SetLockedIndex(bestIndex);
        return true;
    }

    public bool LockNextTarget()
    {
        if (targets.Count == 0)
            return false;
        SetLockedIndex(HasLockedTarget ? (lockedIndex + 1) % targets.Count : 0);
        return true;
    }

    public int CopyTargetSnapshots(InterstellarPlanetTargetSnapshot[] buffer)
    {
        if (buffer == null || buffer.Length == 0)
            return 0;
        int count = Mathf.Min(buffer.Length, targets.Count);
        for (int index = 0; index < count; index++)
        {
            PlanetTarget target = targets[index];
            Color color = target.definition.hasExplicitPalette
                ? target.definition.surfaceColor
                : target.definition.mapColor;
            color.a = 1f;
            buffer[index] = new InterstellarPlanetTargetSnapshot
            {
                planetId = target.definition.planetId,
                displayName = target.definition.displayName,
                universePosition = target.universePosition,
                distance = target.distance,
                color = color,
                locked = index == lockedIndex,
                visited = target.visited
            };
        }
        return count;
    }

    public Vector3 GetDirectionToUniversePosition(DoubleVector3 universePosition)
    {
        if (runtime == null)
            return Vector3.zero;
        DoubleVector3 ship = runtime.ShipUniversePosition;
        double x = universePosition.x - ship.x;
        double y = universePosition.y - ship.y;
        double z = universePosition.z - ship.z;
        double magnitude = Math.Sqrt(x * x + y * y + z * z);
        if (magnitude <= 0.000001d)
            return Vector3.zero;
        return new Vector3((float)(x / magnitude), (float)(y / magnitude), (float)(z / magnitude));
    }

    void SetLockedIndex(int index)
    {
        lockedIndex = index >= 0 && index < targets.Count ? index : -1;
        RefreshProxySet();
    }

    void RefreshTargets(bool force)
    {
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (runtime == null || manager == null || !manager.IsInterstellarGalaxy)
            return;
        InterstellarCoordinate sector = GetShipSector(runtime.ShipUniversePosition);
        if (!force && scanned && sector == lastScanSector)
            return;

        lastScanSector = sector;
        scanned = true;
        string lockedId = LockedPlanet?.planetId;
        ClearTargets();

        for (long z = sector.z - scanRadiusSectors; z <= sector.z + scanRadiusSectors; z++)
        for (long y = sector.y - scanRadiusSectors; y <= sector.y + scanRadiusSectors; y++)
        for (long x = sector.x - scanRadiusSectors; x <= sector.x + scanRadiusSectors; x++)
        {
            var coordinate = new InterstellarCoordinate(x, y, z);
            GalaxyPlanetDefinition definition = manager.GetPlanetAt(coordinate);
            if (definition == null)
                continue;
            DoubleVector3 position = manager.GetInterstellarPlanetPosition(coordinate);
            targets.Add(new PlanetTarget
            {
                definition = definition,
                universePosition = position,
                distance = Distance(runtime.ShipUniversePosition, position),
                visited = IsVisited(manager, definition.planetId)
            });
        }

        targets.Sort((left, right) => left.distance.CompareTo(right.distance));
        if (targets.Count > maximumVisiblePlanets)
        {
            int preservedLockIndex = string.IsNullOrEmpty(lockedId)
                ? -1
                : targets.FindIndex(target => target.definition.planetId == lockedId);
            PlanetTarget preservedLock = preservedLockIndex >= maximumVisiblePlanets
                ? targets[preservedLockIndex]
                : null;
            targets.RemoveRange(maximumVisiblePlanets, targets.Count - maximumVisiblePlanets);
            if (preservedLock != null)
            {
                targets[targets.Count - 1] = preservedLock;
                targets.Sort((left, right) => left.distance.CompareTo(right.distance));
            }
        }
        lockedIndex = string.IsNullOrEmpty(lockedId)
            ? -1
            : targets.FindIndex(target => target.definition.planetId == lockedId);
        RefreshProxySet();
    }

    void UpdateTargetDistances()
    {
        if (runtime == null)
            return;
        DoubleVector3 shipPosition = runtime.ShipUniversePosition;
        bool proxySetChanged = false;
        foreach (PlanetTarget target in targets)
        {
            double previousDistance = target.distance;
            target.distance = Distance(shipPosition, target.universePosition);
            if ((previousDistance <= nearProxyDistance) != (target.distance <= nearProxyDistance))
                proxySetChanged = true;
            if (target.proxy != null)
            {
                target.proxy.transform.position = runtime.ToLocalPosition(target.universePosition);
                target.proxy.SetUniverseTime(GalaxyTravelManager.Instance?.UniverseTimeSeconds ?? 0d);
            }
        }
        if (proxySetChanged)
            RefreshProxySet();
    }

    void RefreshProxySet()
    {
        int nearProxyCount = 0;
        for (int index = 0; index < targets.Count; index++)
        {
            PlanetTarget target = targets[index];
            bool shouldHaveProxy = index == lockedIndex
                || (target.distance <= nearProxyDistance && nearProxyCount++ == 0);
            if (!shouldHaveProxy && target.proxy != null)
            {
                Destroy(target.proxy.gameObject);
                target.proxy = null;
            }
            else if (shouldHaveProxy && target.proxy == null)
            {
                target.proxy = SpacePlanetProxy.Create(
                    proxyRoot,
                    target.definition,
                    planetVisualRadius,
                    planetMaterial,
                    atmosphereMaterial);
                target.proxy.transform.position = runtime.ToLocalPosition(target.universePosition);
            }
        }
    }

    void BeginSelectedPlanetApproach()
    {
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (manager == null || !HasLockedTarget)
            return;
        transitionStarted = true;
        runtime.SaveState(true);
        Rigidbody body = runtime.ShipBody;
        DoubleVector3 relative = runtime.ShipUniversePosition - LockedUniversePosition;
        manager.BeginPlanetApproach(
            targets[lockedIndex].definition,
            relative.ToVector3(),
            body.velocity,
            body.rotation,
            automaticLandingRequested);
    }

    bool TryFindApproachBoundaryTarget(out int targetIndex)
    {
        targetIndex = -1;
        double nearestDistance = double.PositiveInfinity;
        for (int index = 0; index < targets.Count; index++)
        {
            PlanetTarget target = targets[index];
            if (target.distance > GetApproachBoundaryDistance(target.definition)
                || target.distance >= nearestDistance)
                continue;
            nearestDistance = target.distance;
            targetIndex = index;
        }
        return targetIndex >= 0;
    }

    double GetApproachBoundaryDistance(GalaxyPlanetDefinition definition)
    {
        PlanetCelestialProfile celestial = definition?.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        celestial.ClampValues();
        return celestial.surfaceGenerationMode == PlanetSurfaceGenerationMode.StreamingLargeSphere
            ? celestial.radius + 6000d
            : celestial.radius + approachDistance;
    }

    float CalculateAllowedEntrySpeed(GalaxyPlanetDefinition definition, double distance)
    {
        PlanetCelestialProfile celestial = definition?.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        celestial.ClampValues();
        float radius = Mathf.Max(celestial.radius + 1f, (float)distance);
        float physicalEntryLimit = Mathf.Sqrt(2f * celestial.gravitationalParameter / radius) * 0.92f;
        return Mathf.Min(maximumEntrySpeed, Mathf.Max(35f, physicalEntryLimit));
    }

    void HandleOriginShift(Vector3 shift)
    {
        RepositionProxies();
    }

    void HandleUniverseRelocated(DoubleVector3 previousPosition, DoubleVector3 currentPosition)
    {
        scanned = false;
        transitionStarted = false;
        RefreshTargets(true);
        UpdateTargetDistances();
    }

    void RepositionProxies()
    {
        foreach (PlanetTarget target in targets)
        {
            if (target.proxy != null)
                target.proxy.transform.position = runtime.ToLocalPosition(target.universePosition);
        }
    }

    void ClearTargets()
    {
        foreach (PlanetTarget target in targets)
        {
            if (target.proxy != null)
                Destroy(target.proxy.gameObject);
        }
        targets.Clear();
        lockedIndex = -1;
    }

    static bool IsVisited(GalaxyTravelManager manager, string planetId)
    {
        IReadOnlyList<VisitedPlanetRecord> visited = manager.VisitedPlanets;
        for (int index = 0; index < visited.Count; index++)
        {
            if (visited[index] != null && visited[index].planetId == planetId)
                return true;
        }
        return false;
    }

    static InterstellarCoordinate GetShipSector(DoubleVector3 position)
    {
        double spacing = ProceduralInterstellarGenerator.SectorSpacing;
        return new InterstellarCoordinate(
            (long)Math.Round(position.x / spacing, MidpointRounding.AwayFromZero),
            (long)Math.Round(position.y / spacing, MidpointRounding.AwayFromZero),
            (long)Math.Round(position.z / spacing, MidpointRounding.AwayFromZero));
    }

    static double Distance(DoubleVector3 left, DoubleVector3 right)
    {
        double x = left.x - right.x;
        double y = left.y - right.y;
        double z = left.z - right.z;
        return Math.Sqrt(x * x + y * y + z * z);
    }

    static Material CreatePlanetMaterial()
    {
        Shader shader = Shader.Find("Standard");
        var material = new Material(shader) { name = "RuntimeInterstellarPlanet" };
        material.SetFloat("_Glossiness", 0.2f);
        return material;
    }

    static Material CreateAtmosphereMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        var material = new Material(shader) { name = "RuntimeInterstellarAtmosphere" };
        material.color = new Color(0.25f, 0.65f, 1f, 0.13f);
        return material;
    }

    void OnDisable()
    {
        if (runtime != null)
        {
            runtime.OriginShifted -= HandleOriginShift;
            runtime.UniverseRelocated -= HandleUniverseRelocated;
        }
    }

    void OnDestroy()
    {
        ClearTargets();
        if (planetMaterial != null)
            Destroy(planetMaterial);
        if (atmosphereMaterial != null)
            Destroy(atmosphereMaterial);
    }
}

public sealed class SpacePlanetProxy : MonoBehaviour
{
    public GalaxyPlanetDefinition Definition { get; private set; }
    Mesh runtimeMesh;
    PlanetCelestialProfile celestial;
    double universeTimeSeconds;

    public static SpacePlanetProxy Create(
        Transform parent,
        GalaxyPlanetDefinition definition,
        float radius,
        Material sharedPlanetMaterial,
        Material sharedAtmosphereMaterial)
    {
        GameObject planet = new GameObject("PlanetProxy_" + definition.planetId);
        planet.transform.SetParent(parent, false);
        MeshFilter filter = planet.AddComponent<MeshFilter>();
        filter.sharedMesh = PlanetLodMeshBuilder.Build(definition, 14);
        MeshRenderer renderer = planet.AddComponent<MeshRenderer>();

        var proxy = planet.AddComponent<SpacePlanetProxy>();
        proxy.Definition = definition;
        proxy.runtimeMesh = filter.sharedMesh;
        proxy.celestial = (definition.celestial ?? PlanetCelestialProfile.CreateCompatibleDefault()).Clone();
        renderer.sharedMaterial = sharedPlanetMaterial;
        var properties = new MaterialPropertyBlock();
        Color baseColor = definition.hasExplicitPalette ? definition.surfaceColor : definition.mapColor;
        properties.SetColor("_Color", baseColor);
        properties.SetColor("_EmissionColor", Color.Lerp(baseColor, Color.black, 0.82f));
        renderer.SetPropertyBlock(properties);

        PlanetCelestialProfile celestial = proxy.celestial;
        if (!celestial.HasAtmosphere)
            return proxy;

        GameObject atmosphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        atmosphere.name = "Atmosphere";
        atmosphere.transform.SetParent(planet.transform, false);
        atmosphere.transform.localScale = Vector3.one
            * ((celestial.radius + celestial.atmosphereTopAltitude) * 2f);
        Collider atmosphereCollider = atmosphere.GetComponent<Collider>();
        if (atmosphereCollider != null)
            Destroy(atmosphereCollider);
        Renderer atmosphereRenderer = atmosphere.GetComponent<Renderer>();
        atmosphereRenderer.sharedMaterial = sharedAtmosphereMaterial;
        var atmosphereProperties = new MaterialPropertyBlock();
        atmosphereProperties.SetColor("_Color", GetAtmosphereColor(definition.climate));
        atmosphereRenderer.SetPropertyBlock(atmosphereProperties);
        return proxy;
    }

    public void SetUniverseTime(double value)
    {
        universeTimeSeconds = value;
    }

    void LateUpdate()
    {
        if (celestial != null)
            transform.localRotation = PlanetReferenceFrame.RotationAtTime(celestial, universeTimeSeconds);
    }

    void OnDestroy()
    {
        if (runtimeMesh != null)
            Destroy(runtimeMesh);
    }

    static Color GetAtmosphereColor(PlanetClimate climate)
    {
        switch (climate)
        {
            case PlanetClimate.Volcanic: return new Color(1f, 0.22f, 0.05f, 0.12f);
            case PlanetClimate.Crystal: return new Color(0.58f, 0.25f, 1f, 0.14f);
            case PlanetClimate.Tropical: return new Color(0.15f, 0.9f, 0.65f, 0.12f);
            case PlanetClimate.Tundra: return new Color(0.6f, 0.85f, 1f, 0.14f);
            case PlanetClimate.Desert: return new Color(1f, 0.64f, 0.2f, 0.1f);
            default: return new Color(0.25f, 0.65f, 1f, 0.12f);
        }
    }
}
