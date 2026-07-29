using System;
using System.Collections.Generic;
using SpacecraftEditor;
using UnityEngine;
using UnityEngine.Serialization;

public struct InterstellarPlanetTargetSnapshot
{
    public string planetId;
    public string displayName;
    public DoubleVector3 universePosition;
    public UniversePosition universeAddress;
    public double distance;
    public Color color;
    public bool locked;
    public bool visited;
}

public enum PlanetProxyDetail
{
    Far,
    Near
}

[DefaultExecutionOrder(-400)]
[DisallowMultipleComponent]
public sealed class InterstellarNavigationSystem : MonoBehaviour
{
    sealed class PlanetTarget
    {
        public InterstellarCoordinate coordinate;
        public GalaxyPlanetDefinition definition;
        public DoubleVector3 universePosition;
        public UniversePosition universeAddress;
        public SpacePlanetProxy proxy;
        public double distance;
        public bool visited;
    }

    [SerializeField] InterstellarFlightRuntime runtime;
    [SerializeField] SpacecraftIfcsMotor ifcsMotor;
    [SerializeField] Transform proxyRoot;
    [SerializeField, Range(4, 12)] int scanRadiusSectors = 8;
    [SerializeField, Range(1, 16)] int maximumVisiblePlanets = 16;
    [FormerlySerializedAs("planetVisualRadius")]
    [SerializeField, Min(1f)] float farProxyVisualRadiusKm = 280f;
    [SerializeField, Min(100f)] float farProxyPresentationDistanceKm = 8000f;
    [SerializeField, Min(1000f)] float exactPresentationEnterDistanceKm = 80000f;
    [SerializeField, Min(1000f)] float exactPresentationExitDistanceKm = 100000f;
    [FormerlySerializedAs("nearProxyDistance")]
#pragma warning disable 0414
    [SerializeField, HideInInspector] float legacyNearProxyDistanceMeters = 30000f;
#pragma warning restore 0414
    [FormerlySerializedAs("approachDistance")]
    [SerializeField, Min(100f)] float approachDistanceMeters = 720f;
    [FormerlySerializedAs("maximumEntrySpeed")]
    [SerializeField, Min(1f)] float maximumEntrySpeedMetersPerSecond = 260f;

    readonly List<PlanetTarget> targets = new List<PlanetTarget>(16);
    InterstellarCoordinate lastScanSector;
    int lockedIndex = -1;
    bool scanned;
    Material planetMaterial;
    Material atmosphereMaterial;
    bool automaticLandingRequested;
    string nearObservationPlanetId;

    public bool HasTarget => HasLockedTarget;
    public bool HasLockedTarget => lockedIndex >= 0 && lockedIndex < targets.Count;
    public GalaxyPlanetDefinition SelectedPlanet => LockedPlanet;
    public GalaxyPlanetDefinition LockedPlanet => HasLockedTarget ? targets[lockedIndex].definition : null;
    public DoubleVector3 LockedUniversePosition => HasLockedTarget
        ? targets[lockedIndex].universePosition
        : DoubleVector3.Zero;
    public UniversePosition LockedUniverseAddress => HasLockedTarget
        ? targets[lockedIndex].universeAddress
        : default;
    public string TargetName => LockedPlanet == null ? string.Empty : LockedPlanet.displayName;
    public double TargetDistance => HasLockedTarget ? targets[lockedIndex].distance : 0d;
    public int TargetCount => targets.Count;
    public Vector3 DirectionToTarget => GetDirectionToUniversePosition(LockedUniverseAddress);
    public bool AutomaticLandingRequested => automaticLandingRequested;
    public string NearObservationPlanetId => nearObservationPlanetId ?? string.Empty;
    public SpacePlanetProxy LockedProxy => HasLockedTarget ? targets[lockedIndex].proxy : null;
    public Vector3 LockedPlanetVelocity => LockedPlanet == null
        ? Vector3.zero
        : GalaxyTravelManager.Instance?.GetInterstellarPlanetVelocity(
            LockedPlanet.coordinate3D) ?? Vector3.zero;
    public double SelectedApproachBoundaryDistance => HasLockedTarget
        ? GetApproachBoundaryDistance(LockedPlanet)
        : 0d;
    public bool IsNearLockedPlanet => HasLockedTarget
        && IsNearPlanet(LockedPlanet, TargetDistance);

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
        && runtime.ShipRelativeVelocityMetersPerSecond.magnitude
            <= CalculateAllowedEntrySpeed(LockedPlanet, TargetDistance);

    public double GetNearExitDistance(GalaxyPlanetDefinition definition)
    {
        PlanetCelestialProfile celestial = definition?.celestial
            ?? PlanetCelestialProfile.CreateLargeDefault();
        celestial = celestial.Clone();
        PlanetPhysicalProfile physical = celestial.Physical;
        double outerRadius = physical.radiusMeters + Math.Max(
            celestial.maximumTerrainElevation,
            celestial.HasAtmosphere ? physical.atmosphereTopAltitudeMeters : 0d);
        return Math.Max(
            physical.radiusMeters * 4.2d,
            outerRadius + 2_500d);
    }

    void Awake()
    {
        if (runtime == null)
            runtime = FindObjectOfType<InterstellarFlightRuntime>();
        Camera camera = runtime != null && runtime.AstronomicalCamera != null
            ? runtime.AstronomicalCamera
            : null;
        if (camera != null)
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, 120000f);
        if (ifcsMotor == null)
            ifcsMotor = FindObjectOfType<SpacecraftIfcsMotor>();
        if (proxyRoot == null)
            proxyRoot = runtime != null && runtime.AstronomicalRoot != null
                ? GameObject.Find("PlanetRuntimeRoot")?.transform
                    ?? runtime.AstronomicalRoot
                : GameObject.Find("PlanetRuntimeRoot")?.transform ?? transform;
        planetMaterial = CreatePlanetMaterial();
        atmosphereMaterial = CreateAtmosphereMaterial();
        nearObservationPlanetId =
            GalaxyTravelManager.Instance?.NearObservationPlanetId ?? string.Empty;
    }

    void OnEnable()
    {
        if (runtime == null)
            runtime = FindObjectOfType<InterstellarFlightRuntime>();
        if (runtime != null)
        {
            runtime.OriginShifted += HandleOriginShift;
            runtime.UniverseAddressRelocated += HandleUniverseRelocated;
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
    }

    public void RequestAutomaticLanding(bool requested)
    {
        automaticLandingRequested = requested;
    }

    public bool IsNearPlanet(
        GalaxyPlanetDefinition definition,
        double distance)
    {
        return definition != null
            && !string.IsNullOrEmpty(nearObservationPlanetId)
            && string.Equals(
                definition.planetId,
                nearObservationPlanetId,
                StringComparison.Ordinal)
            && distance <= GetNearExitDistance(definition) * 1.35d;
    }

    public void MarkNearPlanet(GalaxyPlanetDefinition definition)
    {
        nearObservationPlanetId = definition?.planetId ?? string.Empty;
        if (definition != null)
        {
            int reachedIndex = targets.FindIndex(target =>
                target.definition != null
                && string.Equals(
                    target.definition.planetId,
                    nearObservationPlanetId,
                    StringComparison.Ordinal));
            if (reachedIndex >= 0)
                lockedIndex = reachedIndex;
            ActivatePlanetCenteredFrame(definition);
        }
        if (Application.isPlaying)
        {
            GalaxyTravelManager.Instance?.SetNearObservationPlanet(
                nearObservationPlanetId,
                true);
        }
        RefreshProxySet();
    }

    public void ClearNearPlanet()
    {
        bool hadNearPlanet = !string.IsNullOrEmpty(nearObservationPlanetId);
        nearObservationPlanetId = string.Empty;
        runtime?.ExitPlanetCenteredFrame();
        ifcsMotor?.ClearVelocityReference();
        if (hadNearPlanet && Application.isPlaying)
        {
            GalaxyTravelManager.Instance?.SetNearObservationPlanet(
                string.Empty,
                true);
        }
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
            Vector3 direction = GetDirectionToUniversePosition(targets[index].universeAddress);
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
                universeAddress = target.universeAddress,
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

    public Vector3 GetDirectionToUniversePosition(UniversePosition universePosition)
    {
        if (runtime == null)
            return Vector3.zero;
        DoubleVector3 delta = UniversePosition.Delta(
            runtime.ShipPhysicalUniversePosition,
            universePosition);
        double magnitude = Math.Sqrt(
            delta.x * delta.x + delta.y * delta.y + delta.z * delta.z);
        if (magnitude <= 0.000001d)
            return Vector3.zero;
        return new Vector3(
            (float)(delta.x / magnitude),
            (float)(delta.y / magnitude),
            (float)(delta.z / magnitude));
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
        InterstellarCoordinate sector = manager.GetInterstellarScanCoordinate(
            runtime.ShipPhysicalUniversePosition);
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
            UniversePosition address = manager.GetInterstellarPlanetAddress(coordinate);
            DoubleVector3 position = address.ToAbsoluteMeters();
            targets.Add(new PlanetTarget
            {
                coordinate = coordinate,
                definition = definition,
                universePosition = position,
                universeAddress = address,
                distance = UniversePosition.Distance(
                    runtime.ShipPhysicalUniversePosition,
                    address),
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
        EnsureNearPlanetTarget(manager);
        lockedIndex = string.IsNullOrEmpty(lockedId)
            ? -1
            : targets.FindIndex(target => target.definition.planetId == lockedId);
        if (lockedIndex < 0 && !string.IsNullOrEmpty(nearObservationPlanetId))
        {
            lockedIndex = targets.FindIndex(target =>
                target.definition != null
                && string.Equals(
                    target.definition.planetId,
                    nearObservationPlanetId,
                    StringComparison.Ordinal));
        }
        RefreshProxySet();
    }

    void EnsureNearPlanetTarget(GalaxyTravelManager manager)
    {
        if (manager == null
            || string.IsNullOrEmpty(nearObservationPlanetId)
            || targets.Exists(target =>
                target.definition != null
                && string.Equals(
                    target.definition.planetId,
                    nearObservationPlanetId,
                    StringComparison.Ordinal))
            || !ProceduralInterstellarGenerator.TryDecodePlanetId(
                nearObservationPlanetId,
                out InterstellarCoordinate coordinate))
        {
            return;
        }

        GalaxyPlanetDefinition definition = manager.GetPlanetAt(coordinate);
        if (definition == null)
            return;
        UniversePosition address =
            manager.GetInterstellarPlanetAddress(coordinate);
        targets.Add(new PlanetTarget
        {
            coordinate = coordinate,
            definition = definition,
            universePosition = address.ToAbsoluteMeters(),
            universeAddress = address,
            distance = UniversePosition.Distance(
                runtime.ShipPhysicalUniversePosition,
                address),
            visited = IsVisited(manager, definition.planetId)
        });
        targets.Sort((left, right) => left.distance.CompareTo(right.distance));
    }

    void UpdateTargetDistances()
    {
        if (runtime == null)
            return;
        UniversePosition shipPosition = runtime.ShipPhysicalUniversePosition;
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        bool proxySetChanged = false;
        foreach (PlanetTarget target in targets)
        {
            if (manager != null)
            {
                target.universeAddress = manager.GetInterstellarPlanetAddress(target.coordinate);
                target.universePosition = target.universeAddress.ToAbsoluteMeters();
            }
            double previousDistance = target.distance;
            target.distance = UniversePosition.Distance(shipPosition, target.universeAddress);
            double exactExitMeters = SpaceKilometerScale.ToMeters(
                exactPresentationExitDistanceKm);
            if ((previousDistance <= exactExitMeters) != (target.distance <= exactExitMeters))
                proxySetChanged = true;
            if (target.proxy != null)
            {
                UpdateProxyPresentation(target);
                target.proxy.SetUniverseTime(GalaxyTravelManager.Instance?.UniverseTimeSeconds ?? 0d);
            }
        }
        ValidateNearObservationPlanet();
        UpdateVelocityReference();
        if (proxySetChanged)
            RefreshProxySet();
    }

    void UpdateVelocityReference()
    {
        PlanetTarget nearTarget = targets.Find(target =>
            target.definition != null
            && string.Equals(
                target.definition.planetId,
                nearObservationPlanetId,
                StringComparison.Ordinal)
            && target.distance <= GetNearExitDistance(target.definition) * 1.35d);
        if (nearTarget == null)
        {
            runtime?.ExitPlanetCenteredFrame();
            ifcsMotor?.ClearVelocityReference();
            return;
        }
        ActivatePlanetCenteredFrame(nearTarget.definition);
        ifcsMotor?.SetVelocityReference(Vector3.zero);
    }

    void ActivatePlanetCenteredFrame(GalaxyPlanetDefinition definition)
    {
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (runtime == null || manager == null || definition == null)
            return;
        runtime.EnterPlanetCenteredFrame(
            definition,
            manager.GetInterstellarPlanetAddress(definition.coordinate3D),
            manager.GetInterstellarPlanetVelocity(definition.coordinate3D));
    }

    void ValidateNearObservationPlanet()
    {
        if (string.IsNullOrEmpty(nearObservationPlanetId))
            return;
        PlanetTarget nearTarget = targets.Find(target =>
            target.definition != null
            && string.Equals(
                target.definition.planetId,
                nearObservationPlanetId,
                StringComparison.Ordinal));
        if (nearTarget == null
            || nearTarget.distance
                > GetNearExitDistance(nearTarget.definition) * 1.35d)
        {
            ClearNearPlanet();
        }
    }

    void RefreshProxySet()
    {
        int nearProxyCount = 0;
        double exactExitMeters = SpaceKilometerScale.ToMeters(
            exactPresentationExitDistanceKm);
        for (int index = 0; index < targets.Count; index++)
        {
            PlanetTarget target = targets[index];
            bool frameTarget = IsPlanetFrameTarget(target);
            bool shouldHaveProxy = frameTarget
                || index == lockedIndex
                || (target.distance <= exactExitMeters && nearProxyCount++ == 0);
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
                    farProxyVisualRadiusKm,
                    planetMaterial,
                    atmosphereMaterial);
                UpdateProxyPresentation(target);
            }
            if (target.proxy != null)
            {
                target.proxy.SetDetail(frameTarget || index == lockedIndex
                    ? PlanetProxyDetail.Near
                    : PlanetProxyDetail.Far);
            }
        }
    }

    void BeginSelectedPlanetApproach()
    {
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (manager == null || !HasLockedTarget)
            return;
        runtime.SaveState(true);
        Rigidbody body = runtime.ShipBody;
        DoubleVector3 physicalRelative = UniversePosition.Delta(
            LockedUniverseAddress,
            runtime.ShipPhysicalUniversePosition);
        Vector3 direction = physicalRelative.ToVector3().normalized;
        PlanetCelestialProfile celestial = LockedPlanet?.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        Vector3 presentationRelative = direction
            * (celestial.radius + 6000f);
        manager.BeginPlanetApproach(
            targets[lockedIndex].definition,
            presentationRelative,
            runtime.ShipRelativeVelocityMetersPerSecond,
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
            ? celestial.Physical.radiusMeters
                + Math.Max(100_000d, celestial.Physical.atmosphereTopAltitudeMeters)
            : celestial.Physical.radiusMeters + approachDistanceMeters;
    }

    float CalculateAllowedEntrySpeed(GalaxyPlanetDefinition definition, double distance)
    {
        PlanetCelestialProfile celestial = definition?.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        celestial.ClampValues();
        double radius = Math.Max(celestial.Physical.radiusMeters + 1d, distance);
        float physicalEntryLimit = (float)(Math.Sqrt(
            2d * celestial.Physical.gravitationalParameter / radius) * 0.92d);
        return Mathf.Min(
            maximumEntrySpeedMetersPerSecond,
            Mathf.Max(35f, physicalEntryLimit));
    }

    void HandleOriginShift(Vector3 shift)
    {
        RepositionProxies();
    }

    void HandleUniverseRelocated(UniversePosition previousPosition, UniversePosition currentPosition)
    {
        scanned = false;
        RefreshTargets(true);
        UpdateTargetDistances();
    }

    void RepositionProxies()
    {
        foreach (PlanetTarget target in targets)
        {
            if (target.proxy != null)
                UpdateProxyPresentation(target);
        }
    }

    void UpdateProxyPresentation(PlanetTarget target)
    {
        if (runtime == null || runtime.ShipBody == null || target?.proxy == null)
            return;
        if (runtime.IsPlanetCenteredFrame)
        {
            DoubleVector3 centeredOffset = IsPlanetFrameTarget(target)
                ? DoubleVector3.Zero
                : UniversePosition.Delta(
                    runtime.PlanetFrameUniversePosition,
                    target.universeAddress);
            PlanetCelestialProfile centeredCelestial =
                target.definition?.celestial
                ?? PlanetCelestialProfile.CreateLargeDefault();
            centeredCelestial.ClampValues();
            float centeredRadiusKm = Mathf.Max(
                0.001f,
                (float)SpaceKilometerScale.ToKilometerUnits(
                    centeredCelestial.Physical.radiusMeters));
            target.proxy.SetKilometerPresentation(
                SpaceKilometerScale.ToKilometerUnits(centeredOffset),
                centeredRadiusKm,
                1f);
            return;
        }
        Vector3 direction = GetDirectionToUniversePosition(target.universeAddress);
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.forward;
        PlanetCelestialProfile celestial = target.definition?.celestial
            ?? PlanetCelestialProfile.CreateLargeDefault();
        celestial.ClampValues();
        float physicalDistanceKm = Mathf.Max(
            0.001f,
            (float)SpaceKilometerScale.ToKilometerUnits(target.distance));
        float enterKm = Mathf.Min(
            exactPresentationEnterDistanceKm,
            exactPresentationExitDistanceKm);
        float exitKm = Mathf.Max(
            exactPresentationEnterDistanceKm,
            exactPresentationExitDistanceKm);
        float exactBlend = 1f - Mathf.InverseLerp(
            enterKm,
            exitKm,
            physicalDistanceKm);
        float farDistanceKm = Mathf.Max(100f, farProxyPresentationDistanceKm);
        float farRadiusKm = PlanetScaleMapping.CalculateProxyRadius(
            celestial,
            target.distance,
            farDistanceKm);
        float physicalRadiusKm = Mathf.Max(
            0.001f,
            (float)SpaceKilometerScale.ToKilometerUnits(
                celestial.Physical.radiusMeters));
        float presentationDistanceKm = Mathf.Lerp(
            farDistanceKm,
            physicalDistanceKm,
            exactBlend);
        float proxyRadiusKm = Mathf.Lerp(
            Mathf.Max(0.001f, farRadiusKm),
            physicalRadiusKm,
            exactBlend);
        target.proxy.SetKilometerPresentation(
            direction * presentationDistanceKm,
            proxyRadiusKm,
            exactBlend);
    }

    bool IsPlanetFrameTarget(PlanetTarget target)
    {
        return runtime != null
            && runtime.IsPlanetCenteredFrame
            && target?.definition != null
            && string.Equals(
                target.definition.planetId,
                runtime.PlanetFrameId,
                StringComparison.Ordinal);
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

    static double Distance(DoubleVector3 left, DoubleVector3 right)
    {
        double x = left.x - right.x;
        double y = left.y - right.y;
        double z = left.z - right.z;
        return Math.Sqrt(x * x + y * y + z * z);
    }

    static Material CreatePlanetMaterial()
    {
        Shader shader = Shader.Find("VoxelPlanet/SurfaceFarLod");
        if (shader == null)
            shader = Shader.Find("Standard");
        var material = new Material(shader) { name = "RuntimeInterstellarPlanet" };
        if (material.HasProperty("_UseLowPolyVisual"))
        {
            material.SetFloat("_UseLowPolyVisual", 1f);
            material.SetFloat("_EnableNearTerrainCutout", 0f);
            material.SetFloat("_RadialInset", 0f);
            material.SetFloat("_MinimumAmbient", 0.23f);
        }
        else if (material.HasProperty("_Glossiness"))
        {
            material.SetFloat("_Glossiness", 0.2f);
        }
        return material;
    }

    static Material CreateAtmosphereMaterial()
    {
        Shader shader = Shader.Find("VoxelPlanet/AtmosphereShell") ?? Shader.Find("Sprites/Default");
        var material = new Material(shader) { name = "RuntimeInterstellarAtmosphere" };
        return material;
    }

    void OnDisable()
    {
        if (runtime != null)
        {
            runtime.OriginShifted -= HandleOriginShift;
            runtime.UniverseAddressRelocated -= HandleUniverseRelocated;
        }
        ifcsMotor?.ClearVelocityReference();
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
    MeshFilter terrainFilter;
    MeshRenderer terrainRenderer;
    MeshRenderer atmosphereRenderer;
    ProceduralPlanetOcean ocean;
    PlanetCelestialProfile celestial;
    PlanetProxyDetail detail = (PlanetProxyDetail)(-1);
    double universeTimeSeconds;
    MaterialPropertyBlock terrainProperties;
    MaterialPropertyBlock atmosphereProperties;

    public PlanetProxyDetail Detail => detail;
    public Mesh TerrainMesh => runtimeMesh;
    public float VisualRadius { get; private set; }
    public float ExactKilometerBlend { get; private set; }

    void Awake()
    {
        terrainProperties = new MaterialPropertyBlock();
        atmosphereProperties = new MaterialPropertyBlock();
    }

    public static SpacePlanetProxy Create(
        Transform parent,
        GalaxyPlanetDefinition definition,
        float radius,
        Material sharedPlanetMaterial,
        Material sharedAtmosphereMaterial)
    {
        GameObject planet = new GameObject("PlanetProxy_" + definition.planetId);
        planet.transform.SetParent(parent, false);
        if (parent != null)
            planet.layer = parent.gameObject.layer;
        MeshFilter filter = planet.AddComponent<MeshFilter>();
        MeshRenderer renderer = planet.AddComponent<MeshRenderer>();

        var proxy = planet.AddComponent<SpacePlanetProxy>();
        proxy.Definition = definition;
        proxy.terrainFilter = filter;
        proxy.terrainRenderer = renderer;
        proxy.celestial = (definition.celestial ?? PlanetCelestialProfile.CreateCompatibleDefault()).Clone();
        renderer.sharedMaterial = sharedPlanetMaterial;
        proxy.SetDetail(PlanetProxyDetail.Far);

        PlanetCelestialProfile celestial = proxy.celestial;
        proxy.ocean = planet.AddComponent<ProceduralPlanetOcean>();
        proxy.ocean.Configure(
            definition,
            Vector3.zero,
            24,
            PlanetOceanRenderMode.Orbital);
        proxy.ApplyTerrainProperties();
        if (!celestial.HasAtmosphere)
        {
            SetLayerRecursively(planet, planet.layer);
            return proxy;
        }

        GameObject atmosphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        atmosphere.name = "Atmosphere";
        atmosphere.transform.SetParent(planet.transform, false);
        atmosphere.layer = planet.layer;
        float visualAtmosphereAltitude = Mathf.Min(
            celestial.atmosphereTopAltitude,
            celestial.radius * 0.1f);
        atmosphere.transform.localScale = Vector3.one
            * ((celestial.radius + visualAtmosphereAltitude) * 2f);
        Collider atmosphereCollider = atmosphere.GetComponent<Collider>();
        if (atmosphereCollider != null)
            Destroy(atmosphereCollider);
        proxy.atmosphereRenderer = atmosphere.GetComponent<MeshRenderer>();
        proxy.atmosphereRenderer.sharedMaterial = sharedAtmosphereMaterial;
        AtmosphereVisualProfile atmosphereVisual = celestial.atmosphereVisual
            ?? new AtmosphereVisualProfile();
        Color atmosphereColor = GetAtmosphereColor(definition.climate);
        proxy.atmosphereProperties.SetVector("_PlanetCenter", planet.transform.position);
        proxy.atmosphereProperties.SetColor("_HorizonColor",
            Color.Lerp(atmosphereVisual.horizonColor, atmosphereColor, 0.35f));
        proxy.atmosphereProperties.SetColor("_ZenithColor", atmosphereVisual.zenithColor);
        proxy.atmosphereProperties.SetColor("_SunsetColor", atmosphereVisual.sunsetColor);
        proxy.atmosphereProperties.SetFloat("_Scattering",
            Mathf.Clamp(atmosphereVisual.scatteringStrength, 0.25f, 1.35f));
        proxy.atmosphereRenderer.SetPropertyBlock(proxy.atmosphereProperties);
        SetLayerRecursively(planet, planet.layer);
        return proxy;
    }

    public void SetUniverseTime(double value)
    {
        universeTimeSeconds = value;
    }

    public void SetVisualRadius(float radius)
    {
        float sourceRadius = celestial == null
            ? PlanetCelestialProfile.CompatibleRadius
            : Mathf.Max(0.01f, celestial.radius);
        VisualRadius = Mathf.Max(0.01f, radius);
        transform.localScale = Vector3.one * (VisualRadius / sourceRadius);
    }

    public void SetKilometerPresentation(
        Vector3 centerKilometerUnits,
        float radiusKilometerUnits,
        float exactBlend)
    {
        transform.position = centerKilometerUnits;
        ExactKilometerBlend = Mathf.Clamp01(exactBlend);
        SetVisualRadius(radiusKilometerUnits);
        ApplyPhysicalAtmosphereRatio();
    }

    public void SetDetail(PlanetProxyDetail value)
    {
        if (detail == value && runtimeMesh != null)
            return;
        detail = value;
        int terrainResolution = value == PlanetProxyDetail.Near ? 64 : 14;
        int oceanResolution = value == PlanetProxyDetail.Near ? 56 : 24;
        Mesh replacement = PlanetLodMeshBuilder.Build(Definition, terrainResolution);
        replacement.name = "SpacePlanetProxy_" + value + "_" + Definition.planetId;
        if (terrainFilter != null)
            terrainFilter.sharedMesh = replacement;
        if (runtimeMesh != null)
            Destroy(runtimeMesh);
        runtimeMesh = replacement;
        if (ocean != null)
            ocean.Configure(
                Definition,
                Vector3.zero,
                oceanResolution,
                PlanetOceanRenderMode.Orbital);
        ApplyTerrainProperties();
    }

    void LateUpdate()
    {
        if (celestial != null)
            transform.localRotation = PlanetReferenceFrame.RotationAtTime(celestial, universeTimeSeconds);
        ApplyTerrainProperties();
        ApplyAtmosphereProperties();
    }

    void OnDestroy()
    {
        if (runtimeMesh != null)
            Destroy(runtimeMesh);
    }

    void ApplyTerrainProperties()
    {
        if (terrainRenderer == null || Definition == null)
            return;
        if (terrainProperties == null)
            terrainProperties = new MaterialPropertyBlock();
        PlanetLowPolyVisualProfile visual = Definition.lowPolyVisual;
        Color baseColor = Definition.hasExplicitPalette
            ? Definition.surfaceColor
            : Definition.mapColor;
        terrainRenderer.GetPropertyBlock(terrainProperties);
        terrainProperties.SetColor("_Color", baseColor);
        terrainProperties.SetColor("_EmissionColor", Color.Lerp(baseColor, Color.black, 0.82f));
        terrainProperties.SetFloat("_EnableNearTerrainCutout", 0f);
        terrainProperties.SetFloat("_UseLowPolyVisual", visual != null ? 1f : 0f);
        terrainProperties.SetFloat("_RadialInset", 0f);
        terrainProperties.SetFloat("_MinimumAmbient", 0.23f);
        terrainProperties.SetVector("_PlanetCenter", transform.position);
        terrainProperties.SetVector("_HideCenter", transform.position);
        terrainProperties.SetFloat("_HideRadius", 0f);
        terrainProperties.SetFloat("_TransitionWidth", 1f);
        terrainProperties.SetFloat("_PlanetRadius", celestial == null ? 100f : celestial.radius);
        terrainProperties.SetFloat(
            "_HeightScale",
            celestial == null ? 12f : Mathf.Max(1f, celestial.maximumTerrainElevation));
        if (visual != null)
        {
            terrainProperties.SetColor("_LowlandColor", visual.lowlandColor);
            terrainProperties.SetColor("_HighlandColor", visual.highlandColor);
            terrainProperties.SetColor("_CliffColor", visual.cliffColor);
            terrainProperties.SetColor("_RockColor", visual.rockColor);
            terrainProperties.SetColor("_AccentColor", visual.accentColor);
            terrainProperties.SetColor("_ShoreColor", visual.shoreColor);
            terrainProperties.SetColor("_SnowColor", visual.snowColor);
            terrainProperties.SetFloat("_FacetStrength", visual.facetStrength);
            terrainProperties.SetFloat("_LightingBands", visual.lightingBands);
            terrainProperties.SetFloat("_MacroColorSize", visual.macroColorSize);
            terrainProperties.SetFloat("_MacroVariation", visual.macroVariation);
            terrainProperties.SetFloat("_CliffSlope", visual.cliffSlope);
            terrainProperties.SetFloat("_SeaLevel", visual.oceanLevel);
            terrainProperties.SetFloat("_ShoreWidth", visual.shoreWidth);
            terrainProperties.SetFloat("_SnowLine", visual.snowLine);
            terrainProperties.SetFloat("_SnowAmount", visual.snowAmount);
        }
        terrainRenderer.SetPropertyBlock(terrainProperties);
    }

    void ApplyAtmosphereProperties()
    {
        if (atmosphereRenderer == null)
            return;
        if (atmosphereProperties == null)
            atmosphereProperties = new MaterialPropertyBlock();
        atmosphereRenderer.GetPropertyBlock(atmosphereProperties);
        atmosphereProperties.SetVector("_PlanetCenter", transform.position);
        atmosphereRenderer.SetPropertyBlock(atmosphereProperties);
    }

    void ApplyPhysicalAtmosphereRatio()
    {
        if (atmosphereRenderer == null || celestial == null)
            return;
        PlanetPhysicalProfile physical = celestial.Physical;
        double radiusMeters = Math.Max(1d, physical.radiusMeters);
        double atmosphereMeters = celestial.HasAtmosphere
            ? Math.Max(0d, physical.atmosphereTopAltitudeMeters)
            : 0d;
        float sourceRadius = Mathf.Max(0.01f, celestial.radius);
        float ratio = (float)((radiusMeters + atmosphereMeters) / radiusMeters);
        atmosphereRenderer.transform.localScale = Vector3.one
            * (sourceRadius * Mathf.Max(1f, ratio) * 2f);
    }

    static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null)
            return;
        root.layer = layer;
        Transform rootTransform = root.transform;
        for (int index = 0; index < rootTransform.childCount; index++)
            SetLayerRecursively(rootTransform.GetChild(index).gameObject, layer);
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
