using System;
using System.Collections;
using SpacecraftEditor;
using UnityEngine;
using UnityEngine.UI;

public enum PlanetApproachFlightPhase
{
    EntryTransit,
    AtmosphericCruise,
    VtolApproach,
    Touchdown,
    SurfaceLoading
}

public enum PlanetAttitudeAssist
{
    Manual,
    Prograde,
    Retrograde,
    SurfaceLevel
}

[DefaultExecutionOrder(-350)]
[DisallowMultipleComponent]
public sealed class PlanetApproachController : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] Transform celestialBodyRoot;
    [SerializeField] MeshFilter planetLod;
    [SerializeField] MeshCollider landingCollision;
    [SerializeField] Renderer atmosphereShell;
    [SerializeField] VoxelQuadSphereWorld approachTerrainWorld;
    [SerializeField] Material terrainSurfaceMaterial;
    [SerializeField] Material terrainRockMaterial;
    [SerializeField] Rigidbody shipBody;
    [SerializeField] SpacecraftIfcsMotor ifcs;
    [SerializeField] SpacecraftLandingGearSystem landingGear;
    [SerializeField] SpacecraftDamageReceiver damageReceiver;
    [SerializeField] CelestialGravityField gravityField;
    [SerializeField] PlanetAtmosphericFlightModel atmosphericFlight;
    [SerializeField] LandingSiteScanner landingSiteScanner;

    [Header("HUD")]
    [SerializeField] Text phaseText;
    [SerializeField] Text orbitText;
    [SerializeField] Text landingText;
    [SerializeField] Text heatText;
    [SerializeField] Text terrainStreamingText;
    [SerializeField] CanvasGroup surfaceLoadingPanel;

    [Header("Entry Transit")]
    [SerializeField, Min(1f)] float minimumEntryDuration = 5f;
    [SerializeField, Range(250f, 650f)] float defaultCruiseHeight = 420f;
    [SerializeField, Min(10f)] float entryAirspeed = 85f;

    [Header("Assisted Flight")]
    [SerializeField, Min(10f)] float minimumCruiseAirspeed = 35f;
    [SerializeField, Min(20f)] float maximumCruiseAirspeed = 120f;
    [SerializeField, Min(1f)] float airspeedChangeRate = 28f;
    [SerializeField, Range(5f, 60f)] float maximumClimbAngle = 22f;
    [SerializeField, Range(5f, 60f)] float maximumAutomaticBank = 32f;
    [SerializeField, Range(5f, 90f)] float maximumTurnRate = 42f;
    [SerializeField, Min(10f)] float vtolHeight = 120f;
    [SerializeField, Min(5f)] float vtolAirspeed = 35f;
    [SerializeField, Min(20f)] float nearTerrainLodHideRadius = 130f;

    [Header("Landing")]
    [SerializeField, Min(0.1f)] float safeRadialSpeed = 2f;
    [SerializeField, Min(0.1f)] float safeTangentialSpeed = 1.5f;
    [SerializeField, Range(1f, 45f)] float safeSlope = 20f;
    [SerializeField, Min(0.1f)] float stableContactDuration = 1f;
    [SerializeField, Min(0f)] float surfaceLoadFadeDuration = 2f;

    GalaxyPlanetDefinition planet;
    PlanetCelestialProfile celestial;
    PlanetAttitudeAssist attitudeAssist = PlanetAttitudeAssist.SurfaceLevel;
    PlanetApproachFlightPhase phase = PlanetApproachFlightPhase.EntryTransit;
    bool landingAssist;
    bool completingLanding;
    float entryElapsed;
    float targetAirspeed;
    float stableLandingTime;
    float targetCruiseHeight;
    Vector3 cruiseForward;
    Mesh runtimePlanetMesh;
    Material runtimePlanetMaterial;
    Rigidbody celestialBody;
    double approachStartUniverseTime;

    public PlanetApproachFlightPhase Phase => phase;
    public OrbitalState Orbit { get; private set; }
    public bool AutomaticLandingActive => landingAssist;
    public float LandingSiteSlope => landingSiteScanner == null ? 90f : landingSiteScanner.GroundSlope;

    void Awake()
    {
        RenderSettings.skybox = null;
        RenderSettings.fog = false;
        Camera camera = Camera.main;
        if (camera != null)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, 30000f);
        }
        ResolveReferences();
    }

    void Start()
    {
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        planet = PlanetApproachContext.IsValid ? PlanetApproachContext.Planet : manager?.CurrentPlanet;
        if (planet == null)
        {
            var generator = new ProceduralInterstellarGenerator(7319, Array.Empty<GalaxyResourceCatalogEntry>());
            planet = generator.GeneratePlanet(InterstellarCoordinate.Zero);
        }
        if (planet == null
            || shipBody == null
            || approachTerrainWorld == null
            || ifcs == null
            || landingGear == null
            || gravityField == null
            || atmosphericFlight == null
            || landingSiteScanner == null)
        {
            Debug.LogError("PlanetApproachController: required planet approach references are missing.", this);
            enabled = false;
            return;
        }

        celestial = (planet.celestial ?? PlanetCelestialProfile.CreateLargeDefault()).Clone();
        celestial.ClampValues();
        approachStartUniverseTime = manager != null ? manager.UniverseTimeSeconds : 0d;
        gravityField.Configure(celestialBodyRoot, celestial);
        UpdatePlanetRotation();
        BuildPlanetLod();

        Vector3 entryLocalDirection = PlanetApproachContext.IsValid
            ? PlanetApproachContext.EntryDirection
            : Vector3.forward;
        if (entryLocalDirection.sqrMagnitude < 0.001f)
            entryLocalDirection = Vector3.forward;
        entryLocalDirection.Normalize();
        targetCruiseHeight = PlanetApproachContext.IsValid
            ? PlanetApproachContext.TargetCruiseHeight
            : defaultCruiseHeight;
        targetCruiseHeight = Mathf.Clamp(targetCruiseHeight, 250f, 650f);

        Vector3 radialUp = celestialBodyRoot.TransformDirection(entryLocalDirection).normalized;
        Vector3 incomingVelocity = PlanetApproachContext.IsValid
            ? PlanetApproachContext.InertialVelocity
            : Vector3.zero;
        cruiseForward = Vector3.ProjectOnPlane(incomingVelocity, radialUp).normalized;
        if (cruiseForward.sqrMagnitude < 0.001f)
        {
            Vector3 localForward = Vector3.Cross(celestial.rotationAxis, entryLocalDirection);
            cruiseForward = celestialBodyRoot.TransformDirection(localForward).normalized;
        }
        cruiseForward = Vector3.ProjectOnPlane(cruiseForward, radialUp).normalized;
        if (cruiseForward.sqrMagnitude < 0.001f)
            cruiseForward = BuildTangent(radialUp);

        float surfaceRadius = GetSurfaceRadiusLocal(entryLocalDirection);
        shipBody.position = celestialBodyRoot.position
            + radialUp * (surfaceRadius + targetCruiseHeight + 20f);
        shipBody.rotation = Quaternion.LookRotation(cruiseForward, radialUp);
        shipBody.velocity = gravityField.SampleAtmosphereVelocity(shipBody.position)
            + cruiseForward * entryAirspeed;
        shipBody.angularVelocity = Vector3.zero;
        damageReceiver?.SetIntegrity(PlanetApproachContext.IsValid
            ? PlanetApproachContext.HullIntegrity
            : 100f);
        damageReceiver?.SetEnvironmentCollisionDamageEnabled(true);

        landingAssist = false;
        landingGear.Configure(shipBody, celestialBodyRoot.position);
        landingGear.SetDeployed(false);
        ifcs.ControlsEnabled = true;
        ifcs.LinearControlEnabled = true;
        ifcs.SetAssistMode(SpacecraftAssistMode.Coupled);
        targetAirspeed = entryAirspeed;

        GalaxyPlanetSaveData save = null;
        manager?.TryLoadPlanetSurfaceSnapshot(planet.planetId, out save);
        approachTerrainWorld.SetBaseTerrainMaterials(terrainSurfaceMaterial, terrainRockMaterial);
        approachTerrainWorld.BeginReadOnlyFlightStreaming(
            planet.seed,
            save,
            planet.surfaceColor,
            planet.rockColor,
            planet.terrain,
            celestial,
            shipBody.transform,
            shipBody.velocity);
        atmosphericFlight.Configure(shipBody, gravityField, EstimateReferenceArea());
        landingSiteScanner.Configure(shipBody, celestialBodyRoot, approachTerrainWorld);
        if (surfaceLoadingPanel != null)
        {
            surfaceLoadingPanel.alpha = 0f;
            surfaceLoadingPanel.gameObject.SetActive(false);
        }
        PlanetApproachContext.Clear();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.V))
            attitudeAssist = (PlanetAttitudeAssist)(((int)attitudeAssist + 1) % 4);
        if (Input.GetKeyDown(KeyCode.L))
            SetAutomaticLanding(!landingAssist);
        if (Input.GetKeyDown(KeyCode.G) && landingGear != null)
            landingGear.SetDeployed(!landingGear.Deployed);
        UpdatePlanetLodBlend();
        UpdateHud();
    }

    void FixedUpdate()
    {
        if (shipBody == null || gravityField == null || completingLanding)
            return;

        UpdatePlanetRotation();
        shipBody.AddForce(gravityField.SampleGravity(shipBody.worldCenterOfMass), ForceMode.Acceleration);
        atmosphericFlight.StepPhysics();
        landingSiteScanner.Sample();
        Orbit = gravityField.CalculateOrbit(shipBody.worldCenterOfMass, shipBody.velocity);

        Vector3 atmosphereVelocity = gravityField.SampleAtmosphereVelocity(shipBody.worldCenterOfMass);
        approachTerrainWorld.SetFlightStreamingTarget(
            shipBody.transform,
            shipBody.velocity - atmosphereVelocity);

        if (phase == PlanetApproachFlightPhase.EntryTransit)
            RunEntryTransit(atmosphereVelocity);
        else
            RunPlayerFlight(atmosphereVelocity);

        CheckLanding(atmosphereVelocity);
    }

    void RunEntryTransit(Vector3 atmosphereVelocity)
    {
        entryElapsed += Time.fixedDeltaTime;
        Vector3 radialUp = GetRadialUp();
        cruiseForward = TransportForward(cruiseForward, radialUp);
        float clearance = GetRadarOrProceduralClearance(radialUp);
        float heightError = targetCruiseHeight - clearance;
        float verticalSpeed = Mathf.Clamp(heightError * 0.11f, -18f, 18f);
        float speed = Mathf.Lerp(entryAirspeed, 72f, Mathf.Clamp01(entryElapsed / minimumEntryDuration));
        Vector3 targetVelocity = atmosphereVelocity + cruiseForward * speed + radialUp * verticalSpeed;
        ifcs.SetExternalWorldVelocityTarget(targetVelocity);
        ifcs.SetExternalWorldAttitudeTarget(Quaternion.LookRotation(cruiseForward, radialUp));

        bool durationComplete = entryElapsed >= minimumEntryDuration;
        bool heightReady = Mathf.Abs(heightError) <= 45f;
        bool terrainReady = approachTerrainWorld.IsFlightRegionReady && landingSiteScanner.HasGround;
        if (!durationComplete || !heightReady || !terrainReady)
            return;

        phase = PlanetApproachFlightPhase.AtmosphericCruise;
        targetAirspeed = Mathf.Clamp(speed, minimumCruiseAirspeed, maximumCruiseAirspeed);
    }

    void RunPlayerFlight(Vector3 atmosphereVelocity)
    {
        SpacecraftFlightCommand command = ifcs.CurrentCommand;
        Vector3 radialUp = GetRadialUp();
        cruiseForward = TransportForward(cruiseForward, radialUp);
        float clearance = GetRadarOrProceduralClearance(radialUp);
        bool vtol = landingGear.Deployed
            || clearance < vtolHeight
            || atmosphericFlight.AirSpeed < vtolAirspeed
            || !celestial.HasAtmosphere;
        phase = vtol ? PlanetApproachFlightPhase.VtolApproach : PlanetApproachFlightPhase.AtmosphericCruise;

        targetAirspeed += command.translation.z * airspeedChangeRate * Time.fixedDeltaTime;
        if (command.brake)
            targetAirspeed = Mathf.MoveTowards(targetAirspeed, 0f, airspeedChangeRate * 2.4f * Time.fixedDeltaTime);
        float terrainSpeedLimit = CalculateTerrainLimitedSpeed();
        float modeMaximum = vtol ? Mathf.Min(45f, maximumCruiseAirspeed) : maximumCruiseAirspeed;
        targetAirspeed = Mathf.Clamp(targetAirspeed, 0f, Mathf.Min(modeMaximum, terrainSpeedLimit));
        if (!vtol && targetAirspeed < minimumCruiseAirspeed)
            targetAirspeed = minimumCruiseAirspeed;

        float yawRate = command.vjoy.y * maximumTurnRate;
        cruiseForward = Quaternion.AngleAxis(yawRate * Time.fixedDeltaTime, radialUp) * cruiseForward;
        cruiseForward = Vector3.ProjectOnPlane(cruiseForward, radialUp).normalized;
        Vector3 right = Vector3.Cross(radialUp, cruiseForward).normalized;

        float climbAngle = vtol ? 0f : -command.vjoy.x * maximumClimbAngle;
        float verticalSpeed = command.translation.y * (vtol ? 12f : 8f);
        if (!vtol)
            verticalSpeed += Mathf.Tan(climbAngle * Mathf.Deg2Rad) * targetAirspeed;
        if (landingAssist)
        {
            float minimumDescent = clearance < 35f ? -1.2f : clearance < 100f ? -3f : -8f;
            verticalSpeed = Mathf.Max(verticalSpeed, minimumDescent);
        }

        float sideSpeed = command.translation.x * (vtol ? 10f : 5f);
        if (landingGear.Deployed && clearance < 55f)
            targetAirspeed = Mathf.Min(targetAirspeed, Mathf.Lerp(5f, 18f, Mathf.Clamp01(clearance / 55f)));
        Vector3 targetVelocity = atmosphereVelocity
            + cruiseForward * targetAirspeed
            + right * sideSpeed
            + radialUp * verticalSpeed;
        ifcs.SetExternalWorldVelocityTarget(targetVelocity);

        Vector3 desiredForward = vtol
            ? cruiseForward
            : (cruiseForward + radialUp * Mathf.Tan(climbAngle * Mathf.Deg2Rad)).normalized;
        float bank = vtol ? command.roll * 20f : command.vjoy.y * maximumAutomaticBank + command.roll * 24f;
        Vector3 desiredUp = Quaternion.AngleAxis(bank, desiredForward) * radialUp;
        if (landingAssist || attitudeAssist == PlanetAttitudeAssist.SurfaceLevel)
            desiredUp = Vector3.Slerp(desiredUp, radialUp, landingAssist ? 0.82f : 0.35f).normalized;
        ifcs.SetExternalWorldAttitudeTarget(Quaternion.LookRotation(desiredForward, desiredUp));
    }

    float CalculateTerrainLimitedSpeed()
    {
        if (!approachTerrainWorld.IsFlightRegionReady)
            return 24f;
        float usableDistance = Mathf.Max(0f, approachTerrainWorld.ReadyDistanceAhead - 80f);
        return Mathf.Clamp(usableDistance / 5f, 24f, maximumCruiseAirspeed);
    }

    void CheckLanding(Vector3 atmosphereVelocity)
    {
        if (landingGear == null || !landingGear.Deployed || landingGear.ContactCount < 3)
        {
            stableLandingTime = 0f;
            return;
        }

        Vector3 radialUp = GetRadialUp();
        Vector3 relativeVelocity = shipBody.velocity - atmosphereVelocity;
        float radialSpeed = Mathf.Abs(Vector3.Dot(relativeVelocity, radialUp));
        float tangentSpeed = Vector3.ProjectOnPlane(relativeVelocity, radialUp).magnitude;
        float slope = Vector3.Angle(radialUp, landingGear.AverageGroundNormal);
        bool safe = radialSpeed < safeRadialSpeed
            && tangentSpeed < safeTangentialSpeed
            && shipBody.angularVelocity.magnitude < 0.35f
            && slope < safeSlope;
        stableLandingTime = safe ? stableLandingTime + Time.fixedDeltaTime : 0f;
        phase = PlanetApproachFlightPhase.Touchdown;
        if (stableLandingTime >= stableContactDuration)
            StartCoroutine(CompleteLandingAfterFade());
    }

    IEnumerator CompleteLandingAfterFade()
    {
        completingLanding = true;
        phase = PlanetApproachFlightPhase.SurfaceLoading;
        ifcs.SetExternalWorldVelocityTarget(Vector3.zero);
        Vector3 landingUp = GetRadialUp();
        Vector3 landingForward = Vector3.ProjectOnPlane(shipBody.transform.forward, landingUp).normalized;
        if (landingForward.sqrMagnitude < 0.001f)
            landingForward = BuildTangent(landingUp);
        ifcs.SetExternalWorldAttitudeTarget(Quaternion.LookRotation(
            landingForward,
            landingUp));
        if (surfaceLoadingPanel != null)
        {
            surfaceLoadingPanel.gameObject.SetActive(true);
            float elapsed = 0f;
            while (elapsed < surfaceLoadFadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                surfaceLoadingPanel.alpha = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, surfaceLoadFadeDuration));
                yield return null;
            }
        }
        else if (surfaceLoadFadeDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(surfaceLoadFadeDuration);
        }

        Vector3 worldUp = landingSiteScanner.HasGround
            ? landingSiteScanner.GroundNormal.normalized
            : GetRadialUp();
        Vector3 localDirection = celestialBodyRoot.InverseTransformDirection(worldUp).normalized;
        Vector3 worldPoint = landingSiteScanner.HasGround
            ? landingSiteScanner.GroundPoint
            : celestialBodyRoot.position + worldUp * GetSurfaceRadius(worldUp);
        Vector3 localPoint = celestialBodyRoot.InverseTransformPoint(worldPoint);
        Quaternion localShipRotation = Quaternion.Inverse(celestialBodyRoot.rotation) * shipBody.rotation;
        GalaxyTravelManager.Instance?.CompletePlanetLanding(new PendingPlanetLandingContext
        {
            planetId = planet.planetId,
            coordinate = planet.coordinate3D,
            landingDirection = localDirection,
            shipRotation = localShipRotation,
            playerLocalPosition = localPoint + localDirection * 3f,
            landingPointLocal = localPoint,
            landingGroundNormal = localDirection,
            hullIntegrity = damageReceiver == null ? 100f : damageReceiver.Integrity
        });
    }

    void BuildPlanetLod()
    {
        if (runtimePlanetMesh != null)
            Destroy(runtimePlanetMesh);
        runtimePlanetMesh = PlanetLodMeshBuilder.Build(planet, 48);
        if (planetLod != null)
        {
            planetLod.sharedMesh = runtimePlanetMesh;
            planetLod.transform.localScale = Vector3.one * 0.999f;
            Renderer renderer = planetLod.GetComponent<Renderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("VoxelPlanet/SurfaceFarLod");
                if (shader != null)
                {
                    runtimePlanetMaterial = new Material(shader) { name = "ApproachPlanetFarLod" };
                    renderer.sharedMaterial = runtimePlanetMaterial;
                    ApplyPlanetVisualProfile(runtimePlanetMaterial);
                    UpdatePlanetLodBlend();
                }
            }
        }
        if (celestialBodyRoot != null)
        {
            ProceduralPlanetOcean ocean = celestialBodyRoot.GetComponent<ProceduralPlanetOcean>()
                ?? celestialBodyRoot.gameObject.AddComponent<ProceduralPlanetOcean>();
            ocean.Configure(
                planet,
                Vector3.zero,
                56,
                PlanetOceanRenderMode.Orbital);
        }
        if (landingCollision != null)
        {
            landingCollision.sharedMesh = null;
            landingCollision.enabled = false;
        }
        if (atmosphereShell == null)
            return;
        atmosphereShell.gameObject.SetActive(celestial.HasAtmosphere);
        atmosphereShell.transform.localScale = Vector3.one
            * ((celestial.radius + celestial.atmosphereTopAltitude) * 2f);
        PlanetAtmosphereVisualController visuals = atmosphereShell.GetComponent<PlanetAtmosphereVisualController>()
            ?? atmosphereShell.gameObject.AddComponent<PlanetAtmosphereVisualController>();
        visuals.Configure(celestialBodyRoot, celestial, atmosphereShell);
    }

    void ApplyPlanetVisualProfile(Material material)
    {
        if (material == null || planet == null || planet.lowPolyVisual == null)
            return;

        PlanetLowPolyVisualProfile visual = planet.lowPolyVisual.Clone();
        visual.ClampValues();
        material.SetFloat("_UseLowPolyVisual", 1f);
        material.SetColor("_LowlandColor", visual.lowlandColor);
        material.SetColor("_HighlandColor", visual.highlandColor);
        material.SetColor("_CliffColor", visual.cliffColor);
        material.SetColor("_RockColor", visual.rockColor);
        material.SetColor("_AccentColor", visual.accentColor);
        material.SetColor("_ShoreColor", visual.shoreColor);
        material.SetColor("_SnowColor", visual.snowColor);
        material.SetFloat("_FacetStrength", visual.facetStrength);
        material.SetFloat("_LightingBands", visual.lightingBands);
        material.SetFloat("_MacroColorSize", visual.macroColorSize);
        material.SetFloat("_MacroVariation", visual.macroVariation);
        material.SetFloat("_CliffSlope", visual.cliffSlope);
        material.SetFloat("_PlanetRadius", celestial.radius);
        material.SetFloat("_HeightScale", Mathf.Max(1f, celestial.maximumTerrainElevation));
        material.SetFloat("_SeaLevel", visual.oceanLevel);
        material.SetFloat("_ShoreWidth", visual.shoreWidth);
        material.SetFloat("_SnowLine", visual.snowLine);
        material.SetFloat("_SnowAmount", visual.snowAmount);
        material.SetFloat("_MinimumAmbient", 0.2f);
    }

    public void SetAutomaticLanding(bool enabled)
    {
        landingAssist = enabled;
        if (ifcs == null)
            return;
        ifcs.ControlsEnabled = true;
        ifcs.LinearControlEnabled = true;
        ifcs.SetAssistMode(SpacecraftAssistMode.Coupled);
    }

    float GetRadarOrProceduralClearance(Vector3 radialUp)
    {
        return landingSiteScanner != null && landingSiteScanner.HasGround
            ? landingSiteScanner.RadarAltitude
            : (shipBody.worldCenterOfMass - celestialBodyRoot.position).magnitude - GetSurfaceRadius(radialUp);
    }

    float GetSurfaceRadius(Vector3 worldDirection)
    {
        Vector3 localDirection = celestialBodyRoot.InverseTransformDirection(worldDirection.normalized);
        return GetSurfaceRadiusLocal(localDirection);
    }

    float GetSurfaceRadiusLocal(Vector3 localDirection)
    {
        PlanetTerrainSettings terrain = planet.terrain ?? new PlanetTerrainSettings();
        return celestial.radius + VoxelQuadSphereTerrain.GetSurfaceNoise(
            localDirection.normalized * celestial.radius,
            planet.seed,
            terrain);
    }

    Vector3 GetRadialUp()
    {
        Vector3 offset = shipBody.worldCenterOfMass - celestialBodyRoot.position;
        return offset.sqrMagnitude > 0.001f ? offset.normalized : celestialBodyRoot.up;
    }

    static Vector3 BuildTangent(Vector3 radialUp)
    {
        Vector3 axis = Mathf.Abs(Vector3.Dot(radialUp, Vector3.up)) < 0.9f
            ? Vector3.up
            : Vector3.right;
        return Vector3.Cross(axis, radialUp).normalized;
    }

    static Vector3 TransportForward(Vector3 forward, Vector3 radialUp)
    {
        Vector3 tangent = Vector3.ProjectOnPlane(forward, radialUp);
        return tangent.sqrMagnitude > 0.001f ? tangent.normalized : BuildTangent(radialUp);
    }

    float EstimateReferenceArea()
    {
        Renderer[] renderers = shipBody.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return 12f;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return Mathf.Clamp(bounds.size.x * bounds.size.y, 4f, 80f);
    }

    void UpdatePlanetRotation()
    {
        double universeTime = approachStartUniverseTime + Time.timeAsDouble;
        Quaternion rotation = PlanetReferenceFrame.RotationAtTime(celestial, universeTime);
        if (celestialBody != null)
            celestialBody.MoveRotation(rotation);
        else if (celestialBodyRoot != null)
            celestialBodyRoot.rotation = rotation;
    }

    void UpdatePlanetLodBlend()
    {
        if (runtimePlanetMaterial == null || celestialBodyRoot == null || shipBody == null)
            return;
        Vector3 center = celestialBodyRoot.position;
        Vector3 hideCenter = shipBody.worldCenterOfMass;
        runtimePlanetMaterial.SetVector("_PlanetCenter", new Vector4(center.x, center.y, center.z, 1f));
        runtimePlanetMaterial.SetVector("_HideCenter", new Vector4(hideCenter.x, hideCenter.y, hideCenter.z, 1f));
        runtimePlanetMaterial.SetFloat("_HideRadius", nearTerrainLodHideRadius);
        runtimePlanetMaterial.SetFloat("_RadialInset", 1.5f);
        runtimePlanetMaterial.SetFloat(
            "_EnableNearTerrainCutout",
            approachTerrainWorld != null && approachTerrainWorld.IsFlightRegionReady ? 1f : 0f);
    }

    void UpdateHud()
    {
        float clearance = landingSiteScanner != null && landingSiteScanner.HasGround
            ? landingSiteScanner.RadarAltitude
            : 0f;
        float radialSpeed = 0f;
        float tangentSpeed = 0f;
        if (shipBody != null && gravityField != null)
        {
            Vector3 up = GetRadialUp();
            Vector3 relative = shipBody.velocity - gravityField.SampleAtmosphereVelocity(shipBody.position);
            radialSpeed = Vector3.Dot(relative, up);
            tangentSpeed = Vector3.ProjectOnPlane(relative, up).magnitude;
        }
        if (phaseText != null)
            phaseText.text = $"飞行阶段  {phase}\nL 着陆辅助 {(landingAssist ? "开启" : "关闭")}";
        if (orbitText != null)
            orbitText.text = $"雷达高度 {clearance:F1} m\n切向速度 {tangentSpeed:F1} m/s\n径向速度 {radialSpeed:F1} m/s\n前方地形 {approachTerrainWorld?.ReadyDistanceAhead ?? 0f:F0} m";
        if (landingText != null)
            landingText.text = $"G 起落架 {(landingGear != null && landingGear.Deployed ? "展开" : "收起")}\n地面坡度 {LandingSiteSlope:F1}°\n接触点 {(landingGear == null ? 0 : landingGear.ContactCount)}/3\n预计落点 {landingSiteScanner?.Safety.ToString() ?? "Unknown"}";
        if (heatText != null)
        {
            float heat = atmosphericFlight == null
                ? 0f
                : atmosphericFlight.Density * Mathf.Pow(atmosphericFlight.AirSpeed, 3f) * 0.002f;
            heatText.text = $"空速 {atmosphericFlight?.AirSpeed ?? 0f:F1} m/s\n迎角 {atmosphericFlight?.AngleOfAttack ?? 0f:F1}°\n失速 {(atmosphericFlight != null && atmosphericFlight.IsStalling ? "警告" : "正常")}\n大气密度 {atmosphericFlight?.Density ?? 0f:F3}\n热流 {heat:F1}";
        }
        if (terrainStreamingText != null)
            terrainStreamingText.text = approachTerrainWorld != null && approachTerrainWorld.IsFlightRegionReady
                ? $"地形已就绪  {approachTerrainWorld.ReadyDistanceAhead:F0} m"
                : "正在流送前方真实地形，飞控已限制速度";
    }

    void ResolveReferences()
    {
        if (celestialBodyRoot == null)
            celestialBodyRoot = GameObject.Find("CelestialBodyRoot")?.transform ?? transform;
        if (planetLod == null)
            planetLod = GameObject.Find("PlanetLod")?.GetComponent<MeshFilter>();
        if (landingCollision == null)
            landingCollision = GameObject.Find("LandingCollisionPatch")?.GetComponent<MeshCollider>();
        if (atmosphereShell == null)
            atmosphereShell = GameObject.Find("AtmosphereShell")?.GetComponent<Renderer>();
        if (approachTerrainWorld == null)
            approachTerrainWorld = GameObject.Find("ApproachTerrainWorld")?.GetComponent<VoxelQuadSphereWorld>();
        InterstellarShipController ship = FindObjectOfType<InterstellarShipController>();
        if (shipBody == null)
            shipBody = ship?.ShipBody;
        if (ifcs == null && shipBody != null)
            ifcs = shipBody.GetComponent<SpacecraftIfcsMotor>();
        if (landingGear == null && shipBody != null)
            landingGear = shipBody.GetComponent<SpacecraftLandingGearSystem>()
                ?? shipBody.gameObject.AddComponent<SpacecraftLandingGearSystem>();
        if (damageReceiver == null && shipBody != null)
            damageReceiver = shipBody.GetComponent<SpacecraftDamageReceiver>();
        if (gravityField == null)
            gravityField = GetComponent<CelestialGravityField>() ?? gameObject.AddComponent<CelestialGravityField>();
        if (atmosphericFlight == null)
            atmosphericFlight = GameObject.Find("AtmosphericFlightModel")?.GetComponent<PlanetAtmosphericFlightModel>();
        if (landingSiteScanner == null)
            landingSiteScanner = GameObject.Find("LandingSiteScanner")?.GetComponent<LandingSiteScanner>();
        celestialBody = celestialBodyRoot.GetComponent<Rigidbody>();
        if (celestialBody == null)
            celestialBody = celestialBodyRoot.gameObject.AddComponent<Rigidbody>();
        celestialBody.isKinematic = true;
        celestialBody.useGravity = false;
        phaseText = phaseText ?? GameObject.Find("FlightPhaseText")?.GetComponent<Text>();
        orbitText = orbitText ?? GameObject.Find("OrbitText")?.GetComponent<Text>();
        landingText = landingText ?? GameObject.Find("LandingText")?.GetComponent<Text>();
        heatText = heatText ?? GameObject.Find("HeatText")?.GetComponent<Text>();
        terrainStreamingText = terrainStreamingText ?? GameObject.Find("TerrainStreamingStatus")?.GetComponent<Text>();
        surfaceLoadingPanel = surfaceLoadingPanel ?? GameObject.Find("SurfaceLoadingPanel")?.GetComponent<CanvasGroup>();
    }

    void OnDestroy()
    {
        if (planetLod != null)
            planetLod.sharedMesh = null;
        if (landingCollision != null)
            landingCollision.sharedMesh = null;
        if (runtimePlanetMesh != null)
            Destroy(runtimePlanetMesh);
        if (runtimePlanetMaterial != null)
            Destroy(runtimePlanetMaterial);
    }
}
