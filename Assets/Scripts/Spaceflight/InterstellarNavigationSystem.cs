using System;
using System.Collections.Generic;
using UnityEngine;

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
    }

    [SerializeField] InterstellarFlightRuntime runtime;
    [SerializeField] Transform proxyRoot;
    [SerializeField, Range(4, 12)] int scanRadiusSectors = 8;
    [SerializeField, Range(1, 12)] int maximumVisiblePlanets = 7;
    [SerializeField, Min(100f)] float planetVisualRadius = 280f;
    [SerializeField, Min(100f)] float approachDistance = 720f;
    [SerializeField, Min(1f)] float maximumEntrySpeed = 260f;

    readonly List<PlanetTarget> targets = new List<PlanetTarget>();
    InterstellarCoordinate lastScanSector;
    int selectedIndex;
    bool scanned;
    Material planetMaterial;
    Material atmosphereMaterial;
    bool transitionStarted;

    public bool HasTarget => selectedIndex >= 0 && selectedIndex < targets.Count;
    public GalaxyPlanetDefinition SelectedPlanet => HasTarget ? targets[selectedIndex].definition : null;
    public string TargetName => SelectedPlanet == null ? string.Empty : SelectedPlanet.displayName;
    public double TargetDistance => HasTarget ? targets[selectedIndex].distance : 0d;
    public Vector3 DirectionToTarget
    {
        get
        {
            if (!HasTarget || runtime == null || runtime.ShipBody == null)
                return Vector3.zero;
            return targets[selectedIndex].proxy.transform.position - runtime.ShipBody.position;
        }
    }
    public bool CanEnterSelected => HasTarget
        && TargetDistance <= approachDistance
        && runtime != null
        && runtime.ShipBody != null
        && runtime.ShipBody.velocity.magnitude <= maximumEntrySpeed;

    void Awake()
    {
        approachDistance = 620f;
        maximumEntrySpeed = 45f;
        if (runtime == null)
            runtime = FindObjectOfType<InterstellarFlightRuntime>();
        if (proxyRoot == null)
            proxyRoot = GameObject.Find("PlanetRuntimeRoot")?.transform ?? transform;
        planetMaterial = CreatePlanetMaterial();
        atmosphereMaterial = CreateAtmosphereMaterial();
    }

    void OnEnable()
    {
        if (runtime != null)
            runtime.OriginShifted += HandleOriginShift;
    }

    void Start()
    {
        RefreshTargets(true);
    }

    void Update()
    {
        RefreshTargets(false);
        UpdateTargetDistances();
        if (Input.GetKeyDown(KeyCode.N) && targets.Count > 0)
            selectedIndex = (selectedIndex + 1) % targets.Count;
        if (!transitionStarted && CanEnterSelected)
            BeginSelectedPlanetApproach();
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
        string selectedId = SelectedPlanet?.planetId;
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
                distance = Distance(runtime.ShipUniversePosition, position)
            });
        }

        targets.Sort((left, right) => left.distance.CompareTo(right.distance));
        if (targets.Count > maximumVisiblePlanets)
            targets.RemoveRange(maximumVisiblePlanets, targets.Count - maximumVisiblePlanets);
        for (int index = 0; index < targets.Count; index++)
        {
            PlanetTarget target = targets[index];
            target.proxy = SpacePlanetProxy.Create(
                proxyRoot,
                target.definition,
                planetVisualRadius,
                planetMaterial,
                atmosphereMaterial);
            target.proxy.transform.position = runtime.ToLocalPosition(target.universePosition);
        }

        selectedIndex = 0;
        if (!string.IsNullOrEmpty(selectedId))
        {
            int retained = targets.FindIndex(target => target.definition.planetId == selectedId);
            if (retained >= 0)
                selectedIndex = retained;
        }
    }

    void UpdateTargetDistances()
    {
        if (runtime == null)
            return;
        DoubleVector3 shipPosition = runtime.ShipUniversePosition;
        foreach (PlanetTarget target in targets)
            target.distance = Distance(shipPosition, target.universePosition);
    }

    void BeginSelectedPlanetApproach()
    {
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (manager == null || !CanEnterSelected)
            return;
        transitionStarted = true;
        runtime.SaveState(true);
        Rigidbody body = runtime.ShipBody;
        Vector3 relativePosition = body.position - targets[selectedIndex].proxy.transform.position;
        manager.BeginPlanetApproach(
            targets[selectedIndex].definition,
            relativePosition,
            body.velocity,
            body.rotation);
    }

    void HandleOriginShift(Vector3 shift)
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
            runtime.OriginShifted -= HandleOriginShift;
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

    public static SpacePlanetProxy Create(
        Transform parent,
        GalaxyPlanetDefinition definition,
        float radius,
        Material sharedPlanetMaterial,
        Material sharedAtmosphereMaterial)
    {
        GameObject planet = new GameObject("PlanetProxy_" + definition.planetId);
        planet.name = "PlanetProxy_" + definition.planetId;
        planet.transform.SetParent(parent, false);
        MeshFilter filter = planet.AddComponent<MeshFilter>();
        filter.sharedMesh = PlanetLodMeshBuilder.Build(definition, 14);
        MeshRenderer renderer = planet.AddComponent<MeshRenderer>();

        var proxy = planet.AddComponent<SpacePlanetProxy>();
        proxy.Definition = definition;
        proxy.runtimeMesh = filter.sharedMesh;
        renderer.sharedMaterial = sharedPlanetMaterial;
        var properties = new MaterialPropertyBlock();
        Color baseColor = definition.hasExplicitPalette ? definition.surfaceColor : definition.mapColor;
        properties.SetColor("_Color", baseColor);
        properties.SetColor("_EmissionColor", Color.Lerp(baseColor, Color.black, 0.82f));
        renderer.SetPropertyBlock(properties);

        PlanetCelestialProfile celestial = definition.celestial ?? PlanetCelestialProfile.CreateCompatibleDefault();
        if (!celestial.HasAtmosphere)
            return proxy;

        GameObject atmosphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        atmosphere.name = "Atmosphere";
        atmosphere.transform.SetParent(planet.transform, false);
        atmosphere.transform.localScale = Vector3.one
            * ((celestial.radius + celestial.atmosphereTopAltitude * 0.35f) * 2f);
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
