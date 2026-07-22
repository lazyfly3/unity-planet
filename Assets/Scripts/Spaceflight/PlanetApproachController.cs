using System;
using SpacecraftEditor;
using UnityEngine;
using UnityEngine.UI;

public enum PlanetApproachFlightPhase
{
    Capture,
    StableOrbit,
    Descent,
    Reentry,
    Landing,
    Landed
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
    [SerializeField] Rigidbody shipBody;
    [SerializeField] SpacecraftIfcsMotor ifcs;
    [SerializeField] SpacecraftLandingGearSystem landingGear;
    [SerializeField] SpacecraftDamageReceiver damageReceiver;
    [SerializeField] CelestialGravityField gravityField;

    [Header("HUD")]
    [SerializeField] Text phaseText;
    [SerializeField] Text orbitText;
    [SerializeField] Text landingText;
    [SerializeField] Text heatText;

    [Header("Aerodynamics")]
    [SerializeField, Min(0.1f)] float dragCoefficientArea = 12f;
    [SerializeField, Min(1f)] float maximumDragAcceleration = 45f;

    GalaxyPlanetDefinition planet;
    PlanetCelestialProfile celestial;
    PlanetAttitudeAssist attitudeAssist;
    PlanetApproachFlightPhase phase;
    bool landingAssist;
    float stableLandingTime;
    Vector3 checkpointPosition;
    Vector3 checkpointVelocity;
    Mesh runtimePlanetMesh;

    public PlanetApproachFlightPhase Phase => phase;
    public OrbitalState Orbit { get; private set; }

    void Awake()
    {
        RenderSettings.skybox = null;
        RenderSettings.fog = false;
        Camera camera = Camera.main;
        if (camera != null)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
        }
        ResolveReferences();
    }

    void Start()
    {
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        planet = PlanetApproachContext.IsValid ? PlanetApproachContext.Planet : manager?.CurrentPlanet;
        if (planet == null)
        {
            var developmentGenerator = new ProceduralInterstellarGenerator(
                7319,
                Array.Empty<GalaxyResourceCatalogEntry>());
            planet = developmentGenerator.GeneratePlanet(InterstellarCoordinate.Zero);
        }
        if (planet == null || shipBody == null)
        {
            Debug.LogError("PlanetApproachController: missing planet context or player ship.", this);
            enabled = false;
            return;
        }

        celestial = (planet.celestial ?? PlanetCelestialProfile.CreateCompatibleDefault()).Clone();
        gravityField.Configure(celestialBodyRoot, celestial);
        BuildPlanetLod();

        Vector3 position = PlanetApproachContext.IsValid
            ? PlanetApproachContext.RelativePosition
            : Vector3.forward * (celestial.radius + 420f);
        if (position.sqrMagnitude < Mathf.Pow(celestial.radius + 70f, 2f))
            position = (position.sqrMagnitude > 0.01f ? position.normalized : Vector3.forward)
                * (celestial.radius + 420f);
        shipBody.position = celestialBodyRoot.position + position;
        shipBody.rotation = PlanetApproachContext.IsValid
            ? PlanetApproachContext.ShipRotation
            : Quaternion.LookRotation(Vector3.Cross(Vector3.up, position.normalized), position.normalized);
        shipBody.velocity = PlanetApproachContext.IsValid
            ? PlanetApproachContext.InertialVelocity
            : Vector3.Cross(Vector3.up, position.normalized).normalized
                * Mathf.Sqrt(celestial.gravitationalParameter / position.magnitude);
        shipBody.angularVelocity = Vector3.zero;
        damageReceiver?.SetIntegrity(PlanetApproachContext.IsValid ? PlanetApproachContext.HullIntegrity : 100f);
        landingGear.Configure(shipBody, celestialBodyRoot.position);
        landingGear.SetDeployed(false);
        checkpointPosition = shipBody.position;
        checkpointVelocity = shipBody.velocity;
        PlanetApproachContext.Clear();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.V))
            attitudeAssist = (PlanetAttitudeAssist)(((int)attitudeAssist + 1) % 4);
        if (Input.GetKeyDown(KeyCode.L))
            landingAssist = !landingAssist;
        if (Input.GetKeyDown(KeyCode.G))
            landingGear.SetDeployed(!landingGear.Deployed);
        UpdateHud();
    }

    void FixedUpdate()
    {
        if (shipBody == null || gravityField == null)
            return;

        shipBody.AddForce(gravityField.SampleGravity(shipBody.worldCenterOfMass), ForceMode.Acceleration);
        ApplyAtmosphericDrag();
        Orbit = gravityField.CalculateOrbit(shipBody.worldCenterOfMass, shipBody.velocity);
        UpdatePhase();
        ApplyAssistTargets();
        CheckLanding();
        CheckCrash();
    }

    void ApplyAtmosphericDrag()
    {
        float density = gravityField.SampleAtmosphereDensity(shipBody.worldCenterOfMass);
        if (density <= 0f)
            return;
        Vector3 relativeVelocity = shipBody.velocity - gravityField.SampleAtmosphereVelocity(shipBody.worldCenterOfMass);
        if (relativeVelocity.sqrMagnitude < 0.001f)
            return;
        Vector3 acceleration = -relativeVelocity.normalized
            * (0.5f * density * dragCoefficientArea * relativeVelocity.sqrMagnitude / Mathf.Max(1f, shipBody.mass));
        shipBody.AddForce(Vector3.ClampMagnitude(acceleration, maximumDragAcceleration), ForceMode.Acceleration);
    }

    void UpdatePhase()
    {
        float atmosphereLimit = celestial.atmosphereTopAltitude;
        if (Orbit.altitude < 12f)
            phase = PlanetApproachFlightPhase.Landing;
        else if (celestial.HasAtmosphere && Orbit.altitude < atmosphereLimit)
            phase = PlanetApproachFlightPhase.Reentry;
        else if (landingAssist)
            phase = PlanetApproachFlightPhase.Descent;
        else if (Orbit.isBound && Orbit.periapsisAltitude > atmosphereLimit + 4f)
            phase = PlanetApproachFlightPhase.StableOrbit;
        else
            phase = PlanetApproachFlightPhase.Capture;
    }

    void ApplyAssistTargets()
    {
        if (ifcs == null)
            return;
        Vector3 radialUp = (shipBody.worldCenterOfMass - celestialBodyRoot.position).normalized;
        Vector3 atmosphereVelocity = gravityField.SampleAtmosphereVelocity(shipBody.worldCenterOfMass);

        if (landingAssist)
        {
            float targetDescent = Orbit.altitude > 45f ? -7f : Orbit.altitude > 18f ? -3.5f : -1.1f;
            Vector3 targetVelocity = atmosphereVelocity + radialUp * targetDescent;
            ifcs.SetExternalWorldVelocityTarget(targetVelocity);
            Vector3 forward = Vector3.ProjectOnPlane(shipBody.transform.forward, radialUp).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.Cross(radialUp, Vector3.right).normalized;
            ifcs.SetExternalWorldAttitudeTarget(Quaternion.LookRotation(forward, radialUp));
            return;
        }

        ifcs.ClearExternalTargets();
        Vector3 desiredForward = Vector3.zero;
        if (attitudeAssist == PlanetAttitudeAssist.Prograde)
            desiredForward = shipBody.velocity.normalized;
        else if (attitudeAssist == PlanetAttitudeAssist.Retrograde)
            desiredForward = -shipBody.velocity.normalized;
        else if (attitudeAssist == PlanetAttitudeAssist.SurfaceLevel)
            desiredForward = Vector3.ProjectOnPlane(shipBody.transform.forward, radialUp).normalized;
        if (desiredForward.sqrMagnitude > 0.001f)
            ifcs.SetExternalWorldAttitudeTarget(Quaternion.LookRotation(desiredForward, radialUp));
    }

    void CheckLanding()
    {
        if (!landingGear.Deployed || landingGear.ContactCount < 3)
        {
            stableLandingTime = 0f;
            return;
        }

        Vector3 up = (shipBody.worldCenterOfMass - celestialBodyRoot.position).normalized;
        float radialSpeed = Mathf.Abs(Vector3.Dot(shipBody.velocity, up));
        float tangentSpeed = Vector3.ProjectOnPlane(shipBody.velocity, up).magnitude;
        float slope = Vector3.Angle(up, landingGear.AverageGroundNormal);
        bool safe = radialSpeed < 2f && tangentSpeed < 1.5f
            && shipBody.angularVelocity.magnitude < 0.35f && slope < 20f;
        stableLandingTime = safe ? stableLandingTime + Time.fixedDeltaTime : 0f;
        if (stableLandingTime < 1f)
            return;

        phase = PlanetApproachFlightPhase.Landed;
        enabled = false;
        GalaxyTravelManager.Instance?.CompletePlanetLanding(new PendingPlanetLandingContext
        {
            planetId = planet.planetId,
            coordinate = planet.coordinate3D,
            landingDirection = up,
            shipRotation = shipBody.rotation,
            playerLocalPosition = up * (celestial.radius + 3f),
            hullIntegrity = damageReceiver == null ? 100f : damageReceiver.Integrity
        });
    }

    void CheckCrash()
    {
        if (Orbit.altitude > -3f || shipBody.velocity.magnitude < 12f)
            return;
        if (damageReceiver != null)
            damageReceiver.SetIntegrity(Mathf.Max(1f, damageReceiver.Integrity - 18f));
        shipBody.position = checkpointPosition;
        shipBody.velocity = checkpointVelocity;
        shipBody.angularVelocity = Vector3.zero;
    }

    void BuildPlanetLod()
    {
        if (runtimePlanetMesh != null)
            Destroy(runtimePlanetMesh);
        runtimePlanetMesh = PlanetLodMeshBuilder.Build(planet, 24);
        planetLod.sharedMesh = runtimePlanetMesh;
        landingCollision.sharedMesh = runtimePlanetMesh;
        Renderer renderer = planetLod.GetComponent<Renderer>();
        if (renderer != null)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_Color", planet.surfaceColor);
            renderer.SetPropertyBlock(properties);
        }
        if (atmosphereShell != null)
        {
            atmosphereShell.gameObject.SetActive(celestial.HasAtmosphere);
            atmosphereShell.transform.localScale = Vector3.one
                * ((celestial.radius + celestial.atmosphereTopAltitude * 0.35f) * 2f);
        }
    }

    void OnDestroy()
    {
        if (planetLod != null)
            planetLod.sharedMesh = null;
        if (landingCollision != null)
            landingCollision.sharedMesh = null;
        if (runtimePlanetMesh != null)
            Destroy(runtimePlanetMesh);
    }

    void UpdateHud()
    {
        if (phaseText != null)
            phaseText.text = $"飞行阶段  {phase}\n姿态辅助  {attitudeAssist}";
        if (orbitText != null)
            orbitText.text = $"高度 {Orbit.altitude:F1} m\n速度 {Orbit.speed:F1} m/s\n圆轨 {Orbit.circularSpeed:F1}  逃逸 {Orbit.escapeSpeed:F1}\n近星点 {Orbit.periapsisAltitude:F1}  远星点 {Orbit.apoapsisAltitude:F1}\n偏心率 {Orbit.eccentricity:F3}";
        if (landingText != null)
            landingText.text = $"G 起落架 {(landingGear != null && landingGear.Deployed ? "展开" : "收起")}\nL 着陆辅助 {(landingAssist ? "开启" : "关闭")}\n接触点 {(landingGear == null ? 0 : landingGear.ContactCount)}/3";
        if (heatText != null)
        {
            float density = gravityField == null || shipBody == null ? 0f : gravityField.SampleAtmosphereDensity(shipBody.position);
            float heat = density * shipBody.velocity.sqrMagnitude * shipBody.velocity.magnitude * 0.002f;
            heatText.text = $"大气密度 {density:F3}\n再入热流 {heat:F1}";
        }
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
        phaseText = phaseText ?? GameObject.Find("FlightPhaseText")?.GetComponent<Text>();
        orbitText = orbitText ?? GameObject.Find("OrbitText")?.GetComponent<Text>();
        landingText = landingText ?? GameObject.Find("LandingText")?.GetComponent<Text>();
        heatText = heatText ?? GameObject.Find("HeatText")?.GetComponent<Text>();
    }
}
