using System.Collections;
using System.Collections.Generic;
using SpacecraftEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityPlanet.ModularAssembly;
using UnityPlanet.SpaceStation;

[DefaultExecutionOrder(-650)]
[DisallowMultipleComponent]
public sealed class PlanetOrbitChapterHubController : MonoBehaviour
{
    const string HubSceneName = "InterstellarFlight";
    const string StationSceneName = "SpaceStationUpgradeTest";

    static readonly Color PanelColor =
        new Color(0.005f, 0.055f, 0.08f, 0.94f);
    static readonly Color PanelSecondaryColor =
        new Color(0.015f, 0.105f, 0.145f, 0.93f);
    static readonly Color Cyan =
        new Color(0.03f, 0.88f, 0.9f, 1f);
    static readonly Color CyanDim =
        new Color(0.04f, 0.38f, 0.48f, 1f);
    static readonly Color Orange =
        new Color(1f, 0.48f, 0.08f, 1f);
    static readonly Color TextPrimary =
        new Color(0.82f, 0.96f, 1f, 1f);
    static readonly Color TextSecondary =
        new Color(0.53f, 0.78f, 0.84f, 1f);
    [SerializeField] Vector3 planetPosition =
        new Vector3(72f, 18f, 278f);
    [SerializeField, Min(40f)] float planetVisualRadius = 130f;
    [SerializeField] Vector3 shipPresentationPosition =
        new Vector3(-42f, 0f, 0f);
    [SerializeField] Vector3 cameraPosition =
        new Vector3(0f, 24f, -96f);
    [SerializeField] Vector3 cameraLookPoint =
        new Vector3(18f, 7f, 105f);
    [Header("Landing transition")]
    [SerializeField, Min(0.4f)] float landingAnimationDuration = 1.6f;
    [SerializeField, Min(0f)] float landingArcHeight = 24f;
    [SerializeField, Min(4f)] float landingApproachDistance = 52f;
    [Header("Planet interaction")]
    [SerializeField, Range(0.05f, 0.6f)]
    float planetDragDegreesPerPixel = 0.22f;
    [SerializeField, Range(45f, 89f)] float planetPitchLimit = 82f;

    readonly List<MissionRuntime> missions =
        new List<MissionRuntime>();
    readonly List<GalaxyPlanetDefinition> availablePlanets =
        new List<GalaxyPlanetDefinition>();
    InterstellarFlightRuntime flightRuntime;
    GalaxyTravelManager travelManager;
    GalaxyPlanetDefinition targetPlanet;
    SpacePlanetProxy planetProxy;
    Material planetMaterial;
    Material atmosphereMaterial;
    Material beaconMaterial;
    Material starMaterial;
    Camera worldCamera;
    Canvas canvas;
    RectTransform canvasRect;
    Text planetNameText;
    Text chapterText;
    Text planetIndexText;
    Text detailTitleText;
    Text detailBodyText;
    Text statusText;
    RectTransform missionDetailPanel;
    Button enterButton;
    Button returnButton;
    Button previousPlanetButton;
    Button nextPlanetButton;
    Rigidbody displayedBody;
    InterstellarShipController displayedShip;
    TrailRenderer landingTrail;
    Material landingTrailMaterial;
    bool shipReady;
    bool landingAnimationPlaying;
    bool transitionStarted;
    bool legacyPlanetProxiesHidden;
    bool interactionEnabled = true;
    int selectedIndex = -1;
    int selectedPlanetIndex;
    GameObject planetRoot;
    PlanetOrbitDragSurface planetDragSurface;
    float planetViewYaw;
    float planetViewPitch;

    public bool IsReady { get; private set; }
    public bool ShipControlsSuppressed { get; private set; }
    public string SelectedMissionId => !HasSelectedMission
        ? string.Empty
        : missions[selectedIndex].Definition.Id;

    bool HasSelectedMission =>
        selectedIndex >= 0 && selectedIndex < missions.Count;

    void Awake()
    {
        if (!string.Equals(
                gameObject.scene.name,
                HubSceneName,
                System.StringComparison.Ordinal))
        {
            enabled = false;
            return;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        PlanetOrbitChapterSelectionContext.Clear();
        SuppressLegacySpaceGameplay();
    }

    IEnumerator Start()
    {
        travelManager = GalaxyTravelManager.Instance;
        targetPlanet = ResolveTargetPlanet(travelManager);
        BuildAvailablePlanets(targetPlanet);
        worldCamera = Camera.main;
        if (worldCamera == null)
        {
            Fail("近轨道场景缺少主摄像机。");
            yield break;
        }

        ConfigureScenePresentation();
        CreatePlanetPresentation();
        CreateMissionDefinitions();
        CreateInterface();
        CreateMissionPresentation();
        ClearMissionSelection();

        flightRuntime = FindObjectOfType<InterstellarFlightRuntime>();
        int remainingFrames = 1800;
        while (remainingFrames-- > 0 &&
               (flightRuntime == null || !flightRuntime.IsReady ||
                flightRuntime.ShipBody == null))
        {
            if (flightRuntime == null)
            {
                flightRuntime =
                    FindObjectOfType<InterstellarFlightRuntime>();
            }
            SuppressLegacySpaceGameplay();
            yield return null;
        }

        if (flightRuntime == null || !flightRuntime.IsReady ||
            flightRuntime.ShipBody == null)
        {
            Fail("模块飞船未能在近轨道大厅完成载入。");
            yield break;
        }

        FreezeAndPresentShip(flightRuntime);
        SuppressLegacySpaceGameplay();
        IsReady = true;
        if (statusText != null)
        {
            statusText.text =
                "按住鼠标左键拖动星球；背面的任务点旋转到正面后才会显示。";
        }
    }

    void Update()
    {
        if (!enabled || transitionStarted)
        {
            return;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        KeepShipFrozen();

        if (Input.GetKeyDown(KeyCode.N) ||
            Input.GetKeyDown(KeyCode.RightArrow) ||
            Input.GetKeyDown(KeyCode.D))
        {
            SelectRelativeMission(1);
        }
        else if (Input.GetKeyDown(KeyCode.LeftArrow) ||
                 Input.GetKeyDown(KeyCode.A))
        {
            SelectRelativeMission(-1);
        }
        else if (Input.GetKeyDown(KeyCode.E) ||
                 Input.GetKeyDown(KeyCode.PageDown))
        {
            SelectPlanet(selectedPlanetIndex + 1);
        }
        else if (Input.GetKeyDown(KeyCode.Q) ||
                 Input.GetKeyDown(KeyCode.PageUp))
        {
            SelectPlanet(selectedPlanetIndex - 1);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            SelectMission(0);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            SelectMission(1);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            SelectMission(2);
        }
        else if (Input.GetKeyDown(KeyCode.Return) ||
                 Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            TryEnterSelectedMission();
        }
        else if (Input.GetKeyDown(KeyCode.Escape))
        {
            ReturnToStation();
        }
    }

    void LateUpdate()
    {
        if (!enabled || canvasRect == null || worldCamera == null)
        {
            return;
        }

        UpdateMissionMarkerPositions();
        KeepShipFrozen();
    }

    public void RotatePlanetByPointerDelta(Vector2 pointerDelta)
    {
        if (!interactionEnabled || transitionStarted || planetRoot == null ||
            worldCamera == null)
            return;
        planetViewYaw = Mathf.Repeat(
            planetViewYaw - pointerDelta.x * planetDragDegreesPerPixel +
            180f,
            360f) - 180f;
        planetViewPitch = Mathf.Clamp(
            planetViewPitch - pointerDelta.y * planetDragDegreesPerPixel,
            -planetPitchLimit,
            planetPitchLimit);
        ApplyPlanetViewRotation();
        UpdateMissionMarkerPositions();
    }

    void ApplyPlanetViewRotation()
    {
        if (planetRoot == null)
            return;
        Vector3 yawAxis = worldCamera == null
            ? Vector3.up
            : worldCamera.transform.up;
        Vector3 pitchAxis = worldCamera == null
            ? Vector3.right
            : worldCamera.transform.right;
        planetRoot.transform.rotation =
            Quaternion.AngleAxis(planetViewYaw, yawAxis) *
            Quaternion.AngleAxis(planetViewPitch, pitchAxis);
    }

    void SuppressLegacySpaceGameplay()
    {
        foreach (PirateEncounterDirector director in
                 FindObjectsOfType<PirateEncounterDirector>(true))
        {
            director.enabled = false;
        }
        foreach (PirateShipAiController pirate in
                 FindObjectsOfType<PirateShipAiController>(true))
        {
            pirate.gameObject.SetActive(false);
        }
        foreach (AsteroidFieldSystem asteroids in
                 FindObjectsOfType<AsteroidFieldSystem>(true))
        {
            asteroids.enabled = false;
        }
        foreach (CelestialGravityField gravity in
                 FindObjectsOfType<CelestialGravityField>(true))
        {
            gravity.enabled = false;
        }
        foreach (InterstellarNavigationSystem navigation in
                 FindObjectsOfType<InterstellarNavigationSystem>(true))
        {
            navigation.enabled = false;
        }
        foreach (InterstellarWarpGateController warpGate in
                 FindObjectsOfType<InterstellarWarpGateController>(true))
        {
            warpGate.enabled = false;
        }
        foreach (InterstellarCruiseController cruise in
                 FindObjectsOfType<InterstellarCruiseController>(true))
        {
            cruise.ControlsEnabled = false;
            cruise.enabled = false;
        }
        foreach (InterstellarCameraRig rig in
                 FindObjectsOfType<InterstellarCameraRig>(true))
        {
            rig.enabled = false;
        }
        foreach (SpaceflightCockpitController cockpit in
                 FindObjectsOfType<SpaceflightCockpitController>(true))
        {
            cockpit.enabled = false;
        }
        foreach (InterstellarShipController ship in
                 FindObjectsOfType<InterstellarShipController>(true))
        {
            ship.ControlsEnabled = false;
            ship.SetWeaponControlsEnabled(false);
        }
        foreach (KeyboardMouseFlightInput input in
                 FindObjectsOfType<KeyboardMouseFlightInput>(true))
        {
            input.CaptureEnabled = false;
        }
        foreach (PlayerSpacecraftWeaponInput weaponInput in
                 FindObjectsOfType<PlayerSpacecraftWeaponInput>(true))
        {
            weaponInput.CaptureEnabled = false;
            weaponInput.enabled = false;
        }
        foreach (SpacecraftWeaponSystem weapons in
                 FindObjectsOfType<SpacecraftWeaponSystem>(true))
        {
            weapons.ControlsEnabled = false;
        }
        foreach (WeaponSystemCoordinator weapons in
                 FindObjectsOfType<WeaponSystemCoordinator>(true))
        {
            weapons.ControlsEnabled = false;
            weapons.SetHudVisible(false);
        }
        foreach (RobocraftMotionCoordinator motion in
                 FindObjectsOfType<RobocraftMotionCoordinator>(true))
        {
            motion.ControlsEnabled = false;
            motion.SetDamageDisabled(true);
        }

        foreach (Canvas existingCanvas in
                 FindObjectsOfType<Canvas>(true))
        {
            if (canvas != null && existingCanvas == canvas)
            {
                continue;
            }
            if (existingCanvas.name != "SpaceflightTransitionOverlay")
            {
                existingCanvas.gameObject.SetActive(false);
            }
        }
    }

    void ConfigureScenePresentation()
    {
        RenderSettings.fog = false;
        RenderSettings.ambientMode =
            UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight =
            new Color(0.055f, 0.085f, 0.12f, 1f);

        worldCamera.transform.SetPositionAndRotation(
            cameraPosition,
            Quaternion.LookRotation(
                (cameraLookPoint - cameraPosition).normalized,
                Vector3.up));
        worldCamera.fieldOfView = 55f;
        worldCamera.nearClipPlane = 0.05f;
        worldCamera.farClipPlane = 1200f;

        foreach (Light light in FindObjectsOfType<Light>(true))
        {
            light.enabled = false;
        }
        GameObject keyObject = new GameObject("OrbitHub_KeyLight");
        Light key = keyObject.AddComponent<Light>();
        key.type = LightType.Directional;
        key.color = new Color(0.72f, 0.86f, 1f, 1f);
        key.intensity = 1.15f;
        key.transform.rotation = Quaternion.Euler(28f, -38f, 0f);

        GameObject rimObject = new GameObject("OrbitHub_RimLight");
        Light rim = rimObject.AddComponent<Light>();
        rim.type = LightType.Directional;
        rim.color = new Color(1f, 0.42f, 0.16f, 1f);
        rim.intensity = 0.55f;
        rim.transform.rotation = Quaternion.Euler(8f, 145f, 0f);

        CreateStarfield();
    }

    void CreateStarfield()
    {
        GameObject root = new GameObject("OrbitHub_Starfield");
        ParticleSystem particles = root.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 100000f;
        main.startSpeed = 0f;
        main.startSize = 0.6f;
        main.maxParticles = 420;
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;
        ParticleSystemRenderer renderer =
            particles.GetComponent<ParticleSystemRenderer>();
        Shader shader = Shader.Find("Particles/Standard Unlit") ??
                        Shader.Find("Sprites/Default");
        starMaterial = new Material(shader)
        {
            name = "OrbitHub_StarMaterial",
            color = new Color(0.7f, 0.86f, 1f, 0.82f)
        };
        renderer.sharedMaterial = starMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        var random = new System.Random(48131);
        var values = new ParticleSystem.Particle[420];
        for (int index = 0; index < values.Length; index++)
        {
            Vector3 direction = new Vector3(
                (float)(random.NextDouble() * 2d - 1d),
                (float)(random.NextDouble() * 2d - 1d),
                (float)(random.NextDouble() * 2d - 1d));
            if (direction.sqrMagnitude < 0.01f)
            {
                direction = Vector3.forward;
            }
            direction.Normalize();
            values[index].position =
                cameraPosition + direction * RandomRange(random, 420f, 560f);
            values[index].startSize = RandomRange(random, 0.25f, 1.05f);
            float tint = RandomRange(random, 0.72f, 1f);
            values[index].startColor =
                new Color(tint * 0.78f, tint * 0.9f, tint, 0.82f);
            values[index].remainingLifetime = 100000f;
        }
        particles.SetParticles(values, values.Length);
    }

    void CreatePlanetPresentation()
    {
        DestroyPlanetPresentation();
        if (targetPlanet == null)
        {
            return;
        }

        if (!legacyPlanetProxiesHidden)
        {
            foreach (SpacePlanetProxy existing in
                     FindObjectsOfType<SpacePlanetProxy>(true))
            {
                existing.gameObject.SetActive(false);
            }
            legacyPlanetProxiesHidden = true;
        }

        Shader surfaceShader = Shader.Find("Standard") ??
                               Shader.Find("Diffuse");
        planetMaterial = new Material(surfaceShader)
        {
            name = "OrbitHub_PlanetMaterial"
        };
        Color planetColor = targetPlanet.hasExplicitPalette
            ? targetPlanet.surfaceColor
            : targetPlanet.mapColor;
        planetColor = Color.Lerp(
            planetColor,
            new Color(0.12f, 0.28f, 0.32f, 1f),
            0.18f);
        planetMaterial.color = planetColor;
        if (planetMaterial.HasProperty("_Glossiness"))
        {
            planetMaterial.SetFloat("_Glossiness", 0.12f);
        }
        if (planetMaterial.HasProperty("_Metallic"))
        {
            planetMaterial.SetFloat("_Metallic", 0f);
        }

        Shader atmosphereShader =
            Shader.Find("VoxelPlanet/AtmosphereShell") ??
            Shader.Find("Sprites/Default");
        atmosphereMaterial = new Material(atmosphereShader)
        {
            name = "OrbitHub_AtmosphereMaterial"
        };

        planetRoot = new GameObject("PlanetChapterHub_PlanetRoot");
        planetRoot.transform.position = planetPosition;
        planetViewYaw = 0f;
        planetViewPitch = 0f;
        ApplyPlanetViewRotation();
        planetProxy = SpacePlanetProxy.Create(
            planetRoot.transform,
            targetPlanet,
            planetVisualRadius,
            planetMaterial,
            atmosphereMaterial);
        planetProxy.name = "PlanetChapterHub_" + targetPlanet.planetId;
        planetProxy.SetDetail(PlanetProxyDetail.Near);
        planetProxy.SetUniverseTime(
            travelManager == null
                ? 0d
                : travelManager.UniverseTimeSeconds);
        planetProxy.SetKilometerPresentation(
            planetPosition,
            planetVisualRadius,
            1f);
    }

    void CreateMissionDefinitions()
    {
        missions.Clear();
        const string mineId = "abandoned_mine";
        const string outpostId = "industrial_outpost";
        const string bossId = "modular_boss";
        FinitePlanetMissionRules mineRules =
            FinitePlanetMissionRules.Resolve(
                mineId,
                selectedPlanetIndex);
        FinitePlanetMissionRules outpostRules =
            FinitePlanetMissionRules.Resolve(
                outpostId,
                selectedPlanetIndex);
        FinitePlanetMissionRules bossRules =
            FinitePlanetMissionRules.Resolve(
                bossId,
                selectedPlanetIndex);
        AddMission(new MissionDefinition(
            mineId,
            "废弃采矿区",
            "区域清剿",
            mineRules.DifficultyLabel,
            mineRules.ObjectiveDescription,
            mineRules.GalaxyCoinReward + " 银河币",
            ResolveMissionSurfaceDirection(mineId)));
        AddMission(new MissionDefinition(
            outpostId,
            "敌方工业区",
            "设施突袭",
            outpostRules.DifficultyLabel,
            outpostRules.ObjectiveDescription,
            outpostRules.GalaxyCoinReward + " 银河币",
            ResolveMissionSurfaceDirection(outpostId)));
        AddMission(new MissionDefinition(
            bossId,
            "模块化首领",
            "首领决战",
            bossRules.DifficultyLabel,
            bossRules.ObjectiveDescription,
            bossRules.GalaxyCoinReward + " 银河币",
            ResolveMissionSurfaceDirection(bossId)));
    }

    Vector3 ResolveMissionSurfaceDirection(string missionId)
    {
        int seed = StableMissionSeed(
            travelManager == null ? 0 : travelManager.WorldSeed,
            targetPlanet == null ? string.Empty : targetPlanet.planetId,
            missionId);
        return PlanetOrbitChapterHubGeometry.DirectionFromSeed(seed);
    }

    void BuildAvailablePlanets(
        GalaxyPlanetDefinition initialPlanet)
    {
        availablePlanets.Clear();
        if (travelManager == null)
        {
            AddAvailablePlanet(initialPlanet);
            selectedPlanetIndex = 0;
            return;
        }

        if (travelManager.IsInterstellarGalaxy && initialPlanet != null)
        {
            InterstellarCoordinate coordinate =
                initialPlanet.coordinate3D;
            long systemX = FloorDivide(
                coordinate.x,
                ProceduralInterstellarGenerator.MacroCellSize);
            long systemY = FloorDivide(
                coordinate.y,
                ProceduralInterstellarGenerator.MacroCellSize);
            long systemZ = FloorDivide(
                coordinate.z,
                ProceduralInterstellarGenerator.MacroCellSize);
            long baseX = systemX *
                         ProceduralInterstellarGenerator.MacroCellSize;
            long baseY = systemY *
                         ProceduralInterstellarGenerator.MacroCellSize;
            long baseZ = systemZ *
                         ProceduralInterstellarGenerator.MacroCellSize;

            for (long z = baseZ;
                 z < baseZ + ProceduralInterstellarGenerator.MacroCellSize;
                 z++)
            for (long y = baseY;
                 y < baseY + ProceduralInterstellarGenerator.MacroCellSize;
                 y++)
            for (long x = baseX;
                 x < baseX + ProceduralInterstellarGenerator.MacroCellSize;
                 x++)
            {
                AddAvailablePlanet(travelManager.GetPlanetAt(
                    new InterstellarCoordinate(x, y, z)));
            }

            availablePlanets.Sort((left, right) =>
            {
                int orbitComparison =
                    left.orbitIndex.CompareTo(right.orbitIndex);
                return orbitComparison != 0
                    ? orbitComparison
                    : string.CompareOrdinal(
                        left.planetId,
                        right.planetId);
            });
        }
        else
        {
            IReadOnlyList<GalaxyPlanetDefinition> planets =
                travelManager.Planets;
            if (planets != null)
            {
                for (int index = 0; index < planets.Count; index++)
                {
                    AddAvailablePlanet(planets[index]);
                }
            }
        }

        AddAvailablePlanet(initialPlanet);
        selectedPlanetIndex = Mathf.Max(
            0,
            availablePlanets.FindIndex(planet =>
                planet != null && initialPlanet != null &&
                string.Equals(
                    planet.planetId,
                    initialPlanet.planetId,
                    System.StringComparison.Ordinal)));
        if (availablePlanets.Count > 0)
        {
            targetPlanet = availablePlanets[selectedPlanetIndex];
        }
    }

    void AddAvailablePlanet(GalaxyPlanetDefinition planet)
    {
        if (planet == null || string.IsNullOrWhiteSpace(planet.planetId))
        {
            return;
        }
        for (int index = 0; index < availablePlanets.Count; index++)
        {
            if (string.Equals(
                    availablePlanets[index].planetId,
                    planet.planetId,
                    System.StringComparison.Ordinal))
            {
                return;
            }
        }
        availablePlanets.Add(planet);
    }

    public void SelectPlanet(int index)
    {
        if (transitionStarted || availablePlanets.Count == 0)
        {
            return;
        }

        int wrappedIndex =
            (index % availablePlanets.Count + availablePlanets.Count) %
            availablePlanets.Count;
        if (wrappedIndex == selectedPlanetIndex &&
            targetPlanet == availablePlanets[wrappedIndex])
        {
            return;
        }

        selectedPlanetIndex = wrappedIndex;
        targetPlanet = availablePlanets[selectedPlanetIndex];
        CreatePlanetPresentation();
        DestroyMissionPresentation();
        CreateMissionDefinitions();
        CreateMissionPresentation();
        RefreshPlanetInterface();
        ClearMissionSelection();
        string displayName = travelManager == null
            ? targetPlanet.displayName
            : travelManager.GetPlanetDisplayName(targetPlanet);
        SetStatus("已切换至“" + displayName + "”，请选择降落区域。");
    }

    void RefreshPlanetInterface()
    {
        string displayName = targetPlanet == null
            ? "目标星球不可用"
            : travelManager == null
                ? targetPlanet.displayName
                : travelManager.GetPlanetDisplayName(targetPlanet);
        if (planetNameText != null)
        {
            planetNameText.text = displayName;
        }
        if (chapterText != null)
        {
            chapterText.text = targetPlanet == null
                ? "星球章节\n暂无可用区域"
                : "星球章节  ·  " +
                  GetClimateDisplayName(targetPlanet.climate) +
                  "\n可用区域  " + missions.Count + "/" +
                  missions.Count;
        }
        if (planetIndexText != null)
        {
            planetIndexText.text = availablePlanets.Count <= 1
                ? "当前星球"
                : "星球  " + (selectedPlanetIndex + 1) + " / " +
                  availablePlanets.Count;
        }

        bool canSwitch = availablePlanets.Count > 1;
        if (previousPlanetButton != null)
        {
            previousPlanetButton.interactable = canSwitch;
        }
        if (nextPlanetButton != null)
        {
            nextPlanetButton.interactable = canSwitch;
        }
    }

    static string GetClimateDisplayName(PlanetClimate climate)
    {
        switch (climate)
        {
            case PlanetClimate.TemperateForest:
                return "温带森林";
            case PlanetClimate.Desert:
                return "沙漠";
            case PlanetClimate.Tropical:
                return "热带";
            case PlanetClimate.Tundra:
                return "冻原";
            case PlanetClimate.Volcanic:
                return "火山";
            case PlanetClimate.Crystal:
                return "水晶";
            default:
                return "荒芜";
        }
    }

    static long FloorDivide(long value, long divisor)
    {
        long quotient = value / divisor;
        long remainder = value % divisor;
        return remainder < 0L ? quotient - 1L : quotient;
    }

    void AddMission(MissionDefinition definition)
    {
        missions.Add(new MissionRuntime(definition));
    }

    void CreateMissionPresentation()
    {
        Shader shader = Shader.Find("Standard");
        Sprite markerSprite = Resources.Load<Sprite>(
            "UI/PlanetOrbitHub/OrbitMissionMarker");
        beaconMaterial = new Material(shader)
        {
            name = "OrbitHub_BeaconMaterial",
            color = Cyan
        };
        beaconMaterial.EnableKeyword("_EMISSION");
        beaconMaterial.SetColor("_EmissionColor", Cyan * 1.4f);

        for (int index = 0; index < missions.Count; index++)
        {
            MissionRuntime runtime = missions[index];
            GameObject beacon = GameObject.CreatePrimitive(
                PrimitiveType.Sphere);
            beacon.name = "MissionBeacon_" + runtime.Definition.Id;
            Collider collider = beacon.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
            beacon.transform.localScale = Vector3.one * 4.5f;
            beacon.GetComponent<MeshRenderer>().sharedMaterial =
                beaconMaterial;
            runtime.Beacon = beacon.transform;

            GameObject lineObject = new GameObject(
                "MissionBeaconLine_" + runtime.Definition.Id);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = 0.8f;
            line.endWidth = 0.25f;
            line.sharedMaterial = beaconMaterial;
            line.startColor = Cyan;
            line.endColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.2f);
            runtime.Line = line;

            int capturedIndex = index;
            Button marker = CreateIconButton(
                canvas.transform,
                "MissionMarker_" + runtime.Definition.Id,
                markerSprite,
                new Vector2(96f, 96f));
            marker.onClick.AddListener(
                () => SelectMission(capturedIndex));
            runtime.MarkerRect =
                marker.GetComponent<RectTransform>();
            runtime.MarkerImage = marker.GetComponent<Image>();
        }
    }

    void CreateInterface()
    {
        GameObject canvasObject = new GameObject(
            "PlanetOrbitChapterHubCanvas");
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;
        CanvasScaler scaler =
            canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();
        canvasRect = canvas.GetComponent<RectTransform>();

        GameObject dragObject = new GameObject(
            "PlanetDragSurface",
            typeof(RectTransform),
            typeof(Image),
            typeof(PlanetOrbitDragSurface));
        dragObject.transform.SetParent(canvas.transform, false);
        RectTransform dragRect = dragObject.GetComponent<RectTransform>();
        dragRect.anchorMin = new Vector2(0.18f, 0.10f);
        dragRect.anchorMax = new Vector2(0.82f, 0.94f);
        dragRect.offsetMin = Vector2.zero;
        dragRect.offsetMax = Vector2.zero;
        Image dragImage = dragObject.GetComponent<Image>();
        dragImage.color = Color.clear;
        dragImage.raycastTarget = true;
        planetDragSurface =
            dragObject.GetComponent<PlanetOrbitDragSurface>();
        planetDragSurface.Configure(this);
        dragObject.transform.SetAsFirstSibling();

        Sprite frameSprite = Resources.Load<Sprite>(
            "UI/PlanetOrbitHub/OrbitHubFrame");
        if (frameSprite != null)
        {
            GameObject frameObject = new GameObject(
                "GeneratedHudFrame",
                typeof(RectTransform),
                typeof(Image));
            frameObject.transform.SetParent(canvas.transform, false);
            Image frameImage = frameObject.GetComponent<Image>();
            frameImage.sprite = frameSprite;
            frameImage.color = Color.white;
            frameImage.preserveAspect = false;
            frameImage.raycastTarget = false;
            SetRectStretch(
                frameObject.GetComponent<RectTransform>(),
                Vector2.zero,
                Vector2.zero);
        }

        if (EventSystem.current == null)
        {
            GameObject eventObject = new GameObject(
                "PlanetOrbitChapterHubEventSystem");
            eventObject.AddComponent<EventSystem>();
            eventObject.AddComponent<StandaloneInputModule>();
        }

        Text title = CreateText(
            canvas.transform,
            "HubTitle",
            "近轨道任务选择",
            25,
            TextPrimary,
            TextAnchor.MiddleCenter);
        SetRect(
            title.rectTransform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -26f),
            new Vector2(360f, 48f));

        RectTransform left = CreatePanel(
            canvas.transform,
            "PlanetOverviewPanel",
            PanelColor);
        SetRect(
            left,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(22f, -38f),
            new Vector2(264f, 170f),
            new Vector2(0f, 1f));
        MakeGeneratedFrameContent(left);
        planetNameText = CreateText(
            left,
            "PlanetName",
            targetPlanet == null
                ? "目标星球不可用"
                : travelManager.GetPlanetDisplayName(targetPlanet),
            24,
            TextPrimary,
            TextAnchor.UpperLeft);
        SetRect(
            planetNameText.rectTransform,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(16f, -16f),
            new Vector2(230f, 34f),
            new Vector2(0f, 1f));
        SetTextBestFit(planetNameText, 16, 23);
        chapterText = CreateText(
            left,
            "ChapterInfo",
            "星球章节\n可用区域  2/2",
            17,
            TextSecondary,
            TextAnchor.UpperLeft);
        SetRect(
            chapterText.rectTransform,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(16f, -58f),
            new Vector2(230f, 46f),
            new Vector2(0f, 1f));

        previousPlanetButton = CreateCompactNavigationButton(
            left,
            "PreviousPlanetButton",
            "<");
        SetRect(
            previousPlanetButton.GetComponent<RectTransform>(),
            new Vector2(0f, 0f),
            new Vector2(0f, 0f),
            new Vector2(18f, 14f),
            new Vector2(42f, 30f),
            new Vector2(0f, 0f));
        previousPlanetButton.onClick.AddListener(
            () => SelectPlanet(selectedPlanetIndex - 1));

        planetIndexText = CreateText(
            left,
            "PlanetIndex",
            string.Empty,
            14,
            TextSecondary,
            TextAnchor.MiddleCenter);
        SetRect(
            planetIndexText.rectTransform,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 15f),
            new Vector2(120f, 28f),
            new Vector2(0.5f, 0f));

        nextPlanetButton = CreateCompactNavigationButton(
            left,
            "NextPlanetButton",
            ">");
        SetRect(
            nextPlanetButton.GetComponent<RectTransform>(),
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(-18f, 14f),
            new Vector2(42f, 30f),
            new Vector2(1f, 0f));
        nextPlanetButton.onClick.AddListener(
            () => SelectPlanet(selectedPlanetIndex + 1));

        missionDetailPanel = CreatePanel(
            canvas.transform,
            "MissionDetailPanel",
            PanelColor);
        SetRect(
            missionDetailPanel,
            new Vector2(1f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(-28f, 16f),
            new Vector2(326f, 458f),
            new Vector2(1f, 0.5f));
        MakeGeneratedFrameContent(missionDetailPanel);
        detailTitleText = CreateText(
            missionDetailPanel,
            "MissionTitle",
            string.Empty,
            24,
            Orange,
            TextAnchor.UpperLeft);
        SetRect(
            detailTitleText.rectTransform,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(24f, -48f),
            new Vector2(266f, 36f),
            new Vector2(0f, 1f));
        SetTextBestFit(detailTitleText, 18, 24);
        detailBodyText = CreateText(
            missionDetailPanel,
            "MissionDetails",
            string.Empty,
            18,
            TextPrimary,
            TextAnchor.UpperLeft);
        detailBodyText.lineSpacing = 1.35f;
        SetRect(
            detailBodyText.rectTransform,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(24f, -100f),
            new Vector2(266f, 238f),
            new Vector2(0f, 1f));
        enterButton = CreateMissionActionButton(
            missionDetailPanel,
            "EnterMissionButton",
            "确认降落");
        SetRect(
            enterButton.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 64f),
            new Vector2(270f, 56f),
            new Vector2(0.5f, 0f));
        enterButton.onClick.AddListener(
            () => TryEnterSelectedMission());
        missionDetailPanel.gameObject.SetActive(false);

        returnButton = CreateButton(
            canvas.transform,
            "ReturnStationButton",
            "返回空间站  Esc",
            new Vector2(244f, 72f),
            Color.clear,
            17);
        SetRect(
            returnButton.GetComponent<RectTransform>(),
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(-24f, -38f),
            new Vector2(244f, 72f),
            new Vector2(1f, 1f));
        Outline returnOutline = returnButton.GetComponent<Outline>();
        if (returnOutline != null)
        {
            returnOutline.enabled = false;
        }
        returnButton.onClick.AddListener(
            () => ReturnToStation());

        RectTransform bottom = CreatePanel(
            canvas.transform,
            "ControlStrip",
            PanelColor);
        SetRect(
            bottom,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 18f),
            new Vector2(640f, 68f),
            new Vector2(0.5f, 0f));
        MakeGeneratedFrameContent(bottom);
        Text controls = CreateText(
            bottom,
            "Controls",
            "旋转  左键拖动    星球  Q / E    区域  A / D    降落  Enter    返回  Esc",
            16,
            TextPrimary,
            TextAnchor.MiddleCenter);
        SetRectStretch(
            controls.rectTransform,
            new Vector2(16f, 6f),
            new Vector2(-16f, -6f));

        statusText = CreateText(
            canvas.transform,
            "Status",
            "正在准备星球与模块飞船……",
            16,
            TextSecondary,
            TextAnchor.MiddleCenter);
        SetRect(
            statusText.rectTransform,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 98f),
            new Vector2(640f, 30f),
            new Vector2(0.5f, 0f));

        RefreshPlanetInterface();
    }

    void FreezeAndPresentShip(InterstellarFlightRuntime runtime)
    {
        displayedBody = runtime.ShipBody;
        displayedShip = runtime.ShipController;
        if (displayedShip != null)
        {
            displayedShip.ControlsEnabled = false;
            displayedShip.SetWeaponControlsEnabled(false);
        }
        foreach (RobocraftMotionCoordinator motion in
                 displayedBody.GetComponentsInChildren<
                     RobocraftMotionCoordinator>(true))
        {
            motion.ControlsEnabled = false;
            motion.SetDamageDisabled(true);
        }
        if (!displayedBody.isKinematic)
        {
            displayedBody.velocity = Vector3.zero;
            displayedBody.angularVelocity = Vector3.zero;
        }
        displayedBody.detectCollisions = false;
        displayedBody.isKinematic = true;
        displayedBody.position = shipPresentationPosition;
        displayedBody.rotation = Quaternion.Euler(4f, 20f, -3f);
        Physics.SyncTransforms();
        ShipControlsSuppressed = true;
        shipReady = true;
    }

    void KeepShipFrozen()
    {
        if (!shipReady || displayedBody == null || landingAnimationPlaying)
        {
            return;
        }
        if (!displayedBody.isKinematic)
        {
            displayedBody.velocity = Vector3.zero;
            displayedBody.angularVelocity = Vector3.zero;
            displayedBody.isKinematic = true;
        }
        displayedBody.position = shipPresentationPosition;
        displayedBody.rotation = Quaternion.Euler(4f, 20f, -3f);
        if (displayedShip != null && displayedShip.ControlsEnabled)
        {
            displayedShip.ControlsEnabled = false;
        }
    }

    void UpdateMissionMarkerPositions()
    {
        if (planetProxy == null)
        {
            return;
        }
        Quaternion rotation = planetProxy.transform.rotation;
        for (int index = 0; index < missions.Count; index++)
        {
            MissionRuntime mission = missions[index];
            Vector3 direction =
                rotation * mission.Definition.SurfaceDirection;
            Vector3 surface = planetPosition +
                              direction * planetVisualRadius;
            Vector3 beacon = planetPosition +
                             direction * (planetVisualRadius + 12f);
            bool visible =
                PlanetOrbitChapterHubGeometry.IsSurfaceVisible(
                    direction,
                    planetPosition,
                    worldCamera.transform.position,
                    planetVisualRadius);
            mission.Visible = visible;
            if (mission.Beacon != null)
            {
                mission.Beacon.position = beacon;
                if (mission.Beacon.gameObject.activeSelf != visible)
                    mission.Beacon.gameObject.SetActive(visible);
            }
            if (mission.Line != null)
            {
                mission.Line.SetPosition(0, surface);
                mission.Line.SetPosition(1, beacon);
                if (mission.Line.gameObject.activeSelf != visible)
                    mission.Line.gameObject.SetActive(visible);
            }

            if (mission.MarkerRect == null)
            {
                continue;
            }
            Vector3 screen = worldCamera.WorldToScreenPoint(beacon);
            visible = visible && screen.z > 0f;
            mission.Visible = visible;
            mission.MarkerRect.gameObject.SetActive(visible);
            if (!visible)
            {
                continue;
            }
            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screen,
                null,
                out localPoint);
            localPoint.x = Mathf.Clamp(localPoint.x, -235f, 235f);
            localPoint.y = Mathf.Clamp(localPoint.y, -160f, 245f);
            mission.MarkerRect.anchoredPosition = localPoint;
        }

        if (enterButton != null)
        {
            enterButton.interactable = interactionEnabled &&
                                       HasSelectedMission &&
                                       missions[selectedIndex].Visible;
        }
    }

    void SelectRelativeMission(int direction)
    {
        if (missions.Count == 0)
        {
            return;
        }
        if (!HasSelectedMission)
        {
            SelectMission(direction < 0 ? missions.Count - 1 : 0);
            return;
        }
        SelectMission(selectedIndex + direction);
    }

    void ClearMissionSelection()
    {
        selectedIndex = -1;
        foreach (MissionRuntime runtime in missions)
        {
            if (runtime.MarkerImage != null)
            {
                runtime.MarkerImage.color =
                    new Color(Cyan.r, Cyan.g, Cyan.b, 0.82f);
            }
            if (runtime.MarkerRect != null)
            {
                runtime.MarkerRect.localScale = Vector3.one * 0.82f;
            }
            if (runtime.Line != null)
            {
                runtime.Line.startColor = Cyan;
                runtime.Line.endColor =
                    new Color(Cyan.r, Cyan.g, Cyan.b, 0.22f);
            }
        }
        if (detailTitleText != null)
        {
            detailTitleText.text = string.Empty;
        }
        if (detailBodyText != null)
        {
            detailBodyText.text = string.Empty;
        }
        if (enterButton != null)
        {
            enterButton.interactable = false;
        }
        if (missionDetailPanel != null)
        {
            missionDetailPanel.gameObject.SetActive(false);
        }
    }

    public void SelectMission(int index)
    {
        if (missions.Count == 0)
        {
            return;
        }
        selectedIndex = (index % missions.Count + missions.Count) %
                        missions.Count;
        if (missionDetailPanel != null)
        {
            missionDetailPanel.gameObject.SetActive(true);
        }
        for (int missionIndex = 0;
             missionIndex < missions.Count;
             missionIndex++)
        {
            bool selected = missionIndex == selectedIndex;
            MissionRuntime runtime = missions[missionIndex];
            if (runtime.MarkerImage != null)
            {
                runtime.MarkerImage.color = selected
                    ? Orange
                    : new Color(Cyan.r, Cyan.g, Cyan.b, 0.82f);
            }
            if (runtime.MarkerRect != null)
            {
                runtime.MarkerRect.localScale = selected
                    ? Vector3.one * 1.12f
                    : Vector3.one * 0.82f;
            }
            if (runtime.Line != null)
            {
                Color color = selected ? Orange : Cyan;
                runtime.Line.startColor = color;
                runtime.Line.endColor =
                    new Color(color.r, color.g, color.b, 0.22f);
            }
        }

        MissionDefinition definition =
            missions[selectedIndex].Definition;
        PlanetMissionEnvironmentKind environment =
            ResolveMissionEnvironment(definition);
        if (detailTitleText != null)
        {
            detailTitleText.text = definition.Name;
        }
        if (detailBodyText != null)
        {
            detailBodyText.text =
                "任务类型：" + definition.Type + "\n\n" +
                "作战环境：" +
                PlanetMissionEnvironmentResolver.GetDisplayName(environment) +
                "\n\n" +
                "危险等级：" + definition.Difficulty + "\n\n" +
                "完成条件：" + definition.Condition + "\n\n" +
                "任务奖励：" + definition.Reward;
        }
    }

    public bool TryEnterSelectedMission()
    {
        if (transitionStarted || !HasSelectedMission)
        {
            if (!transitionStarted)
            {
                SetStatus("请先选择一个可见的任务点。");
            }
            return false;
        }
        if (!IsReady || targetPlanet == null || travelManager == null)
        {
            SetStatus("星球或模块飞船尚未准备完成。");
            return false;
        }

        MissionRuntime selectedMission = missions[selectedIndex];
        if (!selectedMission.Visible)
        {
            SetStatus("任务点位于星球背面，请按住鼠标左键拖动星球找到它。");
            return false;
        }
        MissionDefinition selected = selectedMission.Definition;
        transitionStarted = true;
        SetInteractionEnabled(false);
        SetStatus("正在前往“" + selected.Name + "”……");
        StartCoroutine(PlayLandingAnimationAndEnter(selectedMission));
        return true;
    }

    IEnumerator PlayLandingAnimationAndEnter(MissionRuntime mission)
    {
        if (displayedBody != null && planetProxy != null)
        {
            landingAnimationPlaying = true;
            BeginLandingTrail();

            Vector3 start = displayedBody.position;
            Quaternion startRotation = displayedBody.rotation;
            Vector3 surfaceDirection =
                planetProxy.transform.rotation *
                mission.Definition.SurfaceDirection;
            Vector3 target = planetPosition +
                             surfaceDirection *
                             (planetVisualRadius + 1.5f);
            float elapsed = 0f;
            Vector3 startDirection = (start - planetPosition).normalized;
            float surfaceArcDegrees = Vector3.Angle(
                startDirection,
                surfaceDirection);
            float duration = Mathf.Max(0.4f, landingAnimationDuration) *
                             Mathf.Lerp(
                                 1f,
                                 1.65f,
                                 surfaceArcDegrees / 180f);
            Vector3 arcHint = worldCamera.transform.up;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float linear = Mathf.Clamp01(elapsed / duration);
                float progress = linear * linear * (3f - 2f * linear);
                Vector3 position =
                    PlanetOrbitChapterHubGeometry.EvaluateSafeLandingPath(
                        start,
                        planetPosition,
                        surfaceDirection,
                        planetVisualRadius,
                        landingApproachDistance,
                        landingArcHeight,
                        progress,
                        arcHint);
                float lookAheadProgress = Mathf.Min(1f, progress + 0.003f);
                Vector3 lookAhead =
                    PlanetOrbitChapterHubGeometry.EvaluateSafeLandingPath(
                        start,
                        planetPosition,
                        surfaceDirection,
                        planetVisualRadius,
                        landingApproachDistance,
                        landingArcHeight,
                        lookAheadProgress,
                        arcHint);
                Vector3 tangent = lookAhead - position;
                if (tangent.sqrMagnitude < 0.0001f)
                {
                    Vector3 lookBehind =
                        PlanetOrbitChapterHubGeometry.EvaluateSafeLandingPath(
                            start,
                            planetPosition,
                            surfaceDirection,
                            planetVisualRadius,
                            landingApproachDistance,
                            landingArcHeight,
                            Mathf.Max(0f, progress - 0.003f),
                            arcHint);
                    tangent = position - lookBehind;
                }
                displayedBody.position = position;
                if (tangent.sqrMagnitude > 0.0001f)
                {
                    Vector3 radialUp = (position - planetPosition).normalized;
                    Vector3 flightUp = Vector3.ProjectOnPlane(
                        radialUp,
                        tangent);
                    if (flightUp.sqrMagnitude < 0.0001f)
                    {
                        flightUp = Vector3.ProjectOnPlane(
                            worldCamera.transform.up,
                            tangent);
                    }
                    Quaternion flightRotation = Quaternion.LookRotation(
                        tangent.normalized,
                        flightUp.normalized);
                    displayedBody.rotation = Quaternion.Slerp(
                        startRotation,
                        flightRotation,
                        Mathf.SmoothStep(0f, 1f, linear * 1.35f));
                }
                if (mission.MarkerRect != null)
                {
                    float pulse = 1.12f +
                                  Mathf.Sin(linear * Mathf.PI * 8f) * 0.08f;
                    mission.MarkerRect.localScale = Vector3.one * pulse;
                }
                Physics.SyncTransforms();
                yield return null;
            }

            displayedBody.position = target;
            landingAnimationPlaying = false;
            StopLandingTrail();
        }

        CompleteMissionEntry(mission.Definition);
    }

    void CompleteMissionEntry(MissionDefinition selected)
    {
        int seed = StableMissionSeed(
            travelManager.WorldSeed,
            targetPlanet.planetId,
            selected.Id);
        PlanetMissionEnvironmentKind environment =
            PlanetMissionEnvironmentResolver.Resolve(
                travelManager.WorldSeed,
                targetPlanet.planetId,
                selected.Id);
        PlanetOrbitChapterSelectionContext.Set(
            targetPlanet.planetId,
            selected.Id,
            selected.Name,
            selected.SurfaceDirection,
            seed,
            environment,
            selectedPlanetIndex);

        bool accepted;
        if (travelManager.IsInterstellarGalaxy)
        {
            Quaternion landingRotation = Quaternion.FromToRotation(
                Vector3.up,
                selected.SurfaceDirection);
            accepted = travelManager.EnterPlanetSurfaceDirect(
                targetPlanet,
                selected.SurfaceDirection,
                landingRotation,
                travelManager.SpacecraftHullIntegrity);
        }
        else
        {
            travelManager.EnterPlanet(targetPlanet);
            accepted = true;
        }

        if (!accepted)
        {
            PlanetOrbitChapterSelectionContext.Clear();
            transitionStarted = false;
            landingAnimationPlaying = false;
            StopLandingTrail();
            SetInteractionEnabled(true);
            KeepShipFrozen();
            SelectMission(selectedIndex);
            SetStatus("无法进入所选区域，请检查星球存档状态。");
            return;
        }
    }

    PlanetMissionEnvironmentKind ResolveMissionEnvironment(
        MissionDefinition mission)
    {
        if (mission == null || targetPlanet == null ||
            travelManager == null)
        {
            return PlanetMissionEnvironmentKind.Natural;
        }
        return PlanetMissionEnvironmentResolver.Resolve(
            travelManager.WorldSeed,
            targetPlanet.planetId,
            mission.Id);
    }

    void BeginLandingTrail()
    {
        StopLandingTrail();
        if (displayedBody == null)
        {
            return;
        }

        GameObject trailObject = new GameObject("OrbitHub_LandingTrail");
        trailObject.transform.SetParent(displayedBody.transform, false);
        trailObject.transform.localPosition = Vector3.zero;
        landingTrail = trailObject.AddComponent<TrailRenderer>();
        landingTrail.time = 0.42f;
        landingTrail.minVertexDistance = 0.35f;
        landingTrail.widthMultiplier = 2.6f;
        landingTrail.widthCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(1f, 0f));
        landingTrail.startColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.88f);
        landingTrail.endColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0f);
        landingTrail.alignment = LineAlignment.View;
        landingTrail.textureMode = LineTextureMode.Stretch;

        if (landingTrailMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default") ??
                            Shader.Find("Unlit/Color");
            if (shader != null)
            {
                landingTrailMaterial = new Material(shader)
                {
                    name = "OrbitHub_LandingTrailMaterial",
                    color = Cyan
                };
            }
        }
        if (landingTrailMaterial != null)
        {
            landingTrail.sharedMaterial = landingTrailMaterial;
        }
    }

    void StopLandingTrail()
    {
        if (landingTrail == null)
        {
            return;
        }
        landingTrail.emitting = false;
        Destroy(landingTrail.gameObject);
        landingTrail = null;
    }

    static Vector3 EvaluateBezier(
        Vector3 start,
        Vector3 firstControl,
        Vector3 secondControl,
        Vector3 end,
        float progress)
    {
        float inverse = 1f - progress;
        return inverse * inverse * inverse * start +
               3f * inverse * inverse * progress * firstControl +
               3f * inverse * progress * progress * secondControl +
               progress * progress * progress * end;
    }

    static Vector3 EvaluateBezierTangent(
        Vector3 start,
        Vector3 firstControl,
        Vector3 secondControl,
        Vector3 end,
        float progress)
    {
        float inverse = 1f - progress;
        return 3f * inverse * inverse * (firstControl - start) +
               6f * inverse * progress *
               (secondControl - firstControl) +
               3f * progress * progress * (end - secondControl);
    }

    public bool ReturnToStation()
    {
        if (transitionStarted)
        {
            return false;
        }
        transitionStarted = true;
        PlanetOrbitChapterSelectionContext.Clear();
        SpaceStationFlowContext.PrepareOrbitalReturnToStation();
        SetInteractionEnabled(false);
        SetStatus("正在返回空间站停靠区……");
        SceneManager.LoadScene(
            StationSceneName,
            LoadSceneMode.Single);
        return true;
    }

    void SetInteractionEnabled(bool value)
    {
        interactionEnabled = value;
        if (planetDragSurface != null)
        {
            planetDragSurface.enabled = value;
        }
        if (enterButton != null)
        {
            enterButton.interactable = value && HasSelectedMission &&
                                       missions[selectedIndex].Visible;
        }
        if (returnButton != null)
        {
            returnButton.interactable = value;
        }
        if (previousPlanetButton != null)
        {
            previousPlanetButton.interactable =
                value && availablePlanets.Count > 1;
        }
        if (nextPlanetButton != null)
        {
            nextPlanetButton.interactable =
                value && availablePlanets.Count > 1;
        }
        foreach (MissionRuntime mission in missions)
        {
            if (mission.MarkerRect != null)
            {
                Button button =
                    mission.MarkerRect.GetComponent<Button>();
                if (button != null)
                {
                    button.interactable = value && mission.Visible;
                }
            }
        }
    }

    void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message ?? string.Empty;
        }
    }

    void Fail(string message)
    {
        IsReady = false;
        SetStatus(message);
        Debug.LogError("[PlanetOrbitChapterHub] " + message, this);
    }

    static GalaxyPlanetDefinition ResolveTargetPlanet(
        GalaxyTravelManager manager)
    {
        if (manager == null)
        {
            return null;
        }
        GalaxyPlanetDefinition current = manager.CurrentPlanet;
        if (current != null)
        {
            return current;
        }
        IReadOnlyList<GalaxyPlanetDefinition> planets =
            manager.Planets;
        return planets != null && planets.Count > 0
            ? planets[0]
            : null;
    }

    static int StableMissionSeed(
        int worldSeed,
        string planetId,
        string missionId)
    {
        unchecked
        {
            int hash = worldSeed * 397;
            string combined =
                (planetId ?? string.Empty) + "|" +
                (missionId ?? string.Empty);
            for (int index = 0; index < combined.Length; index++)
            {
                hash = hash * 31 + combined[index];
            }
            return hash;
        }
    }

    static float RandomRange(
        System.Random random,
        float minimum,
        float maximum)
    {
        return Mathf.Lerp(
            minimum,
            maximum,
            (float)random.NextDouble());
    }

    static RectTransform CreatePanel(
        Transform parent,
        string name,
        Color color)
    {
        GameObject panel = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Image));
        panel.transform.SetParent(parent, false);
        Image image = panel.GetComponent<Image>();
        image.color = color;
        Outline outline = panel.AddComponent<Outline>();
        outline.effectColor = CyanDim;
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        return panel.GetComponent<RectTransform>();
    }

    static void MakeGeneratedFrameContent(
        RectTransform panel)
    {
        if (panel == null)
        {
            return;
        }
        Image image = panel.GetComponent<Image>();
        if (image != null)
        {
            image.color = Color.clear;
        }
        Outline outline = panel.GetComponent<Outline>();
        if (outline != null)
        {
            outline.enabled = false;
        }
    }

    static Text CreateText(
        Transform parent,
        string name,
        string value,
        int fontSize,
        Color color,
        TextAnchor alignment)
    {
        GameObject textObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>(
            "LegacyRuntime.ttf");
        text.text = value ?? string.Empty;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    static void SetTextBestFit(
        Text text,
        int minimumSize,
        int maximumSize)
    {
        if (text == null)
        {
            return;
        }
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Max(8, minimumSize);
        text.resizeTextMaxSize = Mathf.Max(
            text.resizeTextMinSize,
            maximumSize);
    }

    static Button CreateButton(
        Transform parent,
        string name,
        string label,
        Vector2 size,
        Color color,
        int fontSize)
    {
        GameObject buttonObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect =
            buttonObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        Image image = buttonObject.GetComponent<Image>();
        image.color = color;
        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = Cyan;
        outline.effectDistance = new Vector2(1.2f, -1.2f);
        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.78f, 0.9f, 0.92f, 1f);
        colors.disabledColor = new Color(0.3f, 0.35f, 0.38f, 0.65f);
        button.colors = colors;

        Text text = CreateText(
            buttonObject.transform,
            "Label",
            label,
            fontSize,
            TextPrimary,
            TextAnchor.MiddleCenter);
        SetRectStretch(
            text.rectTransform,
            new Vector2(8f, 4f),
            new Vector2(-8f, -4f));
        return button;
    }

    static Button CreateCompactNavigationButton(
        Transform parent,
        string name,
        string label)
    {
        Button button = CreateButton(
            parent,
            name,
            label,
            new Vector2(42f, 30f),
            new Color(0.015f, 0.15f, 0.19f, 0.96f),
            17);
        Outline outline = button.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = CyanDim;
            outline.effectDistance = new Vector2(1f, -1f);
        }
        Text labelText = button.GetComponentInChildren<Text>();
        if (labelText != null)
        {
            labelText.color = Cyan;
        }
        return button;
    }

    Button CreateMissionActionButton(
        Transform parent,
        string name,
        string label)
    {
        GameObject buttonObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        Image background = buttonObject.GetComponent<Image>();
        background.color = new Color(0.012f, 0.13f, 0.17f, 0.98f);
        Outline border = buttonObject.AddComponent<Outline>();
        border.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.82f);
        border.effectDistance = new Vector2(1.4f, -1.4f);

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.72f, 0.9f, 0.92f, 1f);
        colors.disabledColor = new Color(0.3f, 0.35f, 0.38f, 0.58f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        GameObject topRailObject = new GameObject(
            "TopRail",
            typeof(RectTransform),
            typeof(Image));
        topRailObject.transform.SetParent(buttonObject.transform, false);
        Image topRail = topRailObject.GetComponent<Image>();
        topRail.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.72f);
        topRail.raycastTarget = false;
        SetRect(
            topRailObject.GetComponent<RectTransform>(),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -3f),
            new Vector2(246f, 2f),
            new Vector2(0.5f, 1f));

        GameObject bottomRailObject = new GameObject(
            "BottomRail",
            typeof(RectTransform),
            typeof(Image));
        bottomRailObject.transform.SetParent(buttonObject.transform, false);
        Image bottomRail = bottomRailObject.GetComponent<Image>();
        bottomRail.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.42f);
        bottomRail.raycastTarget = false;
        SetRect(
            bottomRailObject.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 3f),
            new Vector2(246f, 2f),
            new Vector2(0.5f, 0f));

        GameObject accentObject = new GameObject(
            "OrangeAccent",
            typeof(RectTransform),
            typeof(Image));
        accentObject.transform.SetParent(buttonObject.transform, false);
        Image accent = accentObject.GetComponent<Image>();
        accent.color = Orange;
        accent.raycastTarget = false;
        SetRect(
            accentObject.GetComponent<RectTransform>(),
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(7f, 0f),
            new Vector2(3f, 36f));

        GameObject keyChipObject = new GameObject(
            "KeyChip",
            typeof(RectTransform),
            typeof(Image));
        keyChipObject.transform.SetParent(buttonObject.transform, false);
        Image keyChip = keyChipObject.GetComponent<Image>();
        keyChip.color = new Color(0.02f, 0.07f, 0.09f, 0.95f);
        keyChip.raycastTarget = false;
        Outline keyChipOutline = keyChipObject.AddComponent<Outline>();
        keyChipOutline.effectColor = new Color(
            Orange.r,
            Orange.g,
            Orange.b,
            0.72f);
        keyChipOutline.effectDistance = new Vector2(1f, -1f);
        SetRect(
            keyChipObject.GetComponent<RectTransform>(),
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(16f, 0f),
            new Vector2(64f, 30f),
            new Vector2(0f, 0.5f));

        Text keyText = CreateText(
            keyChipObject.transform,
            "KeyHint",
            "ENTER",
            11,
            Orange,
            TextAnchor.MiddleCenter);
        SetRectStretch(
            keyText.rectTransform,
            new Vector2(4f, 2f),
            new Vector2(-4f, -2f));

        Text labelText = CreateText(
            buttonObject.transform,
            "Label",
            label,
            21,
            TextPrimary,
            TextAnchor.MiddleCenter);
        SetRectStretch(
            labelText.rectTransform,
            new Vector2(92f, 6f),
            new Vector2(-28f, -6f));
        SetTextBestFit(labelText, 16, 21);

        Text chevron = CreateText(
            buttonObject.transform,
            "Chevron",
            ">",
            17,
            Cyan,
            TextAnchor.MiddleCenter);
        SetRect(
            chevron.rectTransform,
            new Vector2(1f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(-22f, 0f),
            new Vector2(20f, 30f));
        return button;
    }

    static Button CreateIconButton(
        Transform parent,
        string name,
        Sprite icon,
        Vector2 size)
    {
        GameObject buttonObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect =
            buttonObject.GetComponent<RectTransform>();
        SetRect(
            rect,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            size);

        Image image = buttonObject.GetComponent<Image>();
        image.sprite = icon;
        image.preserveAspect = true;
        image.color = Cyan;
        image.raycastTarget = true;

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        colors.pressedColor = new Color(0.72f, 0.85f, 0.88f, 1f);
        colors.disabledColor = new Color(0.3f, 0.35f, 0.38f, 0.5f);
        button.colors = colors;
        return button;
    }

    static void SetRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 size,
        Vector2? pivot = null)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    static void SetRectStretch(
        RectTransform rect,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    void DestroyMissionPresentation()
    {
        foreach (MissionRuntime mission in missions)
        {
            if (mission.Beacon != null)
            {
                mission.Beacon.gameObject.SetActive(false);
                Destroy(mission.Beacon.gameObject);
                mission.Beacon = null;
            }
            if (mission.Line != null)
            {
                mission.Line.gameObject.SetActive(false);
                Destroy(mission.Line.gameObject);
                mission.Line = null;
            }
            if (mission.MarkerRect != null)
            {
                mission.MarkerRect.gameObject.SetActive(false);
                Destroy(mission.MarkerRect.gameObject);
                mission.MarkerRect = null;
                mission.MarkerImage = null;
            }
        }
        if (beaconMaterial != null)
        {
            Destroy(beaconMaterial);
            beaconMaterial = null;
        }
    }

    void DestroyPlanetPresentation()
    {
        if (planetRoot != null)
        {
            planetRoot.SetActive(false);
            Destroy(planetRoot);
            planetRoot = null;
        }
        planetProxy = null;
        if (planetMaterial != null)
        {
            Destroy(planetMaterial);
            planetMaterial = null;
        }
        if (atmosphereMaterial != null)
        {
            Destroy(atmosphereMaterial);
            atmosphereMaterial = null;
        }
    }

    void OnDestroy()
    {
        StopLandingTrail();
        DestroyMissionPresentation();
        DestroyPlanetPresentation();
        if (starMaterial != null)
        {
            Destroy(starMaterial);
        }
        if (landingTrailMaterial != null)
        {
            Destroy(landingTrailMaterial);
        }
    }

    sealed class MissionDefinition
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string Type;
        public readonly string Difficulty;
        public readonly string Condition;
        public readonly string Reward;
        public readonly Vector3 SurfaceDirection;

        public MissionDefinition(
            string id,
            string name,
            string type,
            string difficulty,
            string condition,
            string reward,
            Vector3 surfaceDirection)
        {
            Id = id;
            Name = name;
            Type = type;
            Difficulty = difficulty;
            Condition = condition;
            Reward = reward;
            SurfaceDirection = surfaceDirection.sqrMagnitude > 0.001f
                ? surfaceDirection.normalized
                : Vector3.up;
        }
    }

    sealed class MissionRuntime
    {
        public readonly MissionDefinition Definition;
        public Transform Beacon;
        public LineRenderer Line;
        public RectTransform MarkerRect;
        public Image MarkerImage;
        public bool Visible;

        public MissionRuntime(MissionDefinition definition)
        {
            Definition = definition;
        }
    }
}

public static class PlanetOrbitChapterHubGeometry
{
    const float SurfaceClearance = 1.5f;
    const float OrbitEntryPortion = 0.22f;
    const float OrbitExitPortion = 0.82f;

    public static Vector3 DirectionFromSeed(int seed)
    {
        var random = new System.Random(seed);
        float vertical = Mathf.Lerp(
            -1f,
            1f,
            (float)random.NextDouble());
        float azimuth = (float)random.NextDouble() * Mathf.PI * 2f;
        float horizontal = (float)System.Math.Sqrt(System.Math.Max(
            0d,
            1d - vertical * vertical));
        return new Vector3(
            (float)System.Math.Cos(azimuth) * horizontal,
            vertical,
            (float)System.Math.Sin(azimuth) * horizontal);
    }

    public static bool IsSurfaceVisible(
        Vector3 surfaceDirection,
        Vector3 planetCenter,
        Vector3 cameraPosition,
        float planetRadius = 0f,
        float horizonDotPadding = 0.01f)
    {
        Vector3 toCamera = cameraPosition - planetCenter;
        if (surfaceDirection.sqrMagnitude < 0.0001f ||
            toCamera.sqrMagnitude < 0.0001f)
            return false;

        float cameraDistance = toCamera.magnitude;
        float radius = Mathf.Max(0f, planetRadius);
        if (cameraDistance <= radius + 0.001f)
            return false;

        // A near camera sees less than a full hemisphere. The exact sphere
        // horizon is dot(normal, centerToCamera) == radius / distance.
        // A small positive padding prevents markers flickering on the limb.
        float horizonDotThreshold =
            radius / cameraDistance + Mathf.Max(0f, horizonDotPadding);
        return Vector3.Dot(
                   surfaceDirection.normalized,
                   toCamera.normalized) >
               Mathf.Clamp(horizonDotThreshold, 0f, 0.999f);
    }

    public static Vector3 EvaluateSafeLandingPath(
        Vector3 start,
        Vector3 planetCenter,
        Vector3 targetSurfaceDirection,
        float planetRadius,
        float approachDistance,
        float arcHeight,
        float progress,
        Vector3 arcAxisHint)
    {
        planetRadius = Mathf.Max(0.01f, planetRadius);
        progress = Mathf.Clamp01(progress);
        Vector3 targetDirection = targetSurfaceDirection.sqrMagnitude >
                                  0.0001f
            ? targetSurfaceDirection.normalized
            : Vector3.up;
        Vector3 startOffset = start - planetCenter;
        float startRadius = startOffset.magnitude;
        Vector3 startDirection = startRadius > 0.0001f
            ? startOffset / startRadius
            : -targetDirection;
        float targetRadius = planetRadius + SurfaceClearance;
        float orbitRadius = planetRadius + Mathf.Max(4f, approachDistance);
        startRadius = Mathf.Max(startRadius, targetRadius);

        if (progress <= OrbitEntryPortion)
        {
            float phase = Smooth01(progress / OrbitEntryPortion);
            return planetCenter + startDirection * Mathf.Lerp(
                startRadius,
                orbitRadius,
                phase);
        }

        if (progress <= OrbitExitPortion)
        {
            float phase = Smooth01(
                (progress - OrbitEntryPortion) /
                (OrbitExitPortion - OrbitEntryPortion));
            Vector3 direction = SlerpDirection(
                startDirection,
                targetDirection,
                phase,
                arcAxisHint);
            float radius = orbitRadius +
                           (float)System.Math.Sin(phase * Mathf.PI) *
                           Mathf.Max(0f, arcHeight);
            return planetCenter + direction * radius;
        }

        float descent = Smooth01(
            (progress - OrbitExitPortion) /
            (1f - OrbitExitPortion));
        return planetCenter + targetDirection * Mathf.Lerp(
            orbitRadius,
            targetRadius,
            descent);
    }

    static Vector3 SlerpDirection(
        Vector3 from,
        Vector3 to,
        float progress,
        Vector3 axisHint)
    {
        from.Normalize();
        to.Normalize();
        float dot = Mathf.Clamp(Vector3.Dot(from, to), -1f, 1f);
        if (dot > 0.9999f)
            return Vector3.Lerp(from, to, progress).normalized;

        Vector3 axis = Vector3.Cross(from, to);
        if (axis.sqrMagnitude < 0.0001f)
        {
            Vector3 tangentHint = Vector3.ProjectOnPlane(axisHint, from);
            if (tangentHint.sqrMagnitude < 0.0001f)
            {
                tangentHint = Vector3.ProjectOnPlane(Vector3.up, from);
            }
            if (tangentHint.sqrMagnitude < 0.0001f)
            {
                tangentHint = Vector3.ProjectOnPlane(Vector3.right, from);
            }
            axis = Vector3.Cross(from, tangentHint.normalized);
        }
        axis.Normalize();
        float angle = (float)System.Math.Acos(dot) * progress;
        float cosine = (float)System.Math.Cos(angle);
        float sine = (float)System.Math.Sin(angle);
        return (from * cosine + Vector3.Cross(axis, from) * sine)
            .normalized;
    }

    static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }
}

public sealed class PlanetOrbitDragSurface :
    MonoBehaviour,
    IInitializePotentialDragHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    PlanetOrbitChapterHubController owner;
    bool dragging;

    public void Configure(PlanetOrbitChapterHubController value)
    {
        owner = value;
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        eventData.useDragThreshold = false;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        dragging = eventData.button == PointerEventData.InputButton.Left;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragging && owner != null)
            owner.RotatePlanetByPointerDelta(eventData.delta);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        dragging = false;
    }

    void OnDisable()
    {
        dragging = false;
    }
}
