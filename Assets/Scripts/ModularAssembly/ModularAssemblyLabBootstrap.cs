using ModularAssembly;
using SpacecraftEditor;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class ModularAssemblyLabBootstrap : MonoBehaviour
{
    [SerializeField] GridModuleDefinition[] definitions;
    [SerializeField] Camera sceneCamera;

    public void ConfigureAssets(GridModuleDefinition[] moduleDefinitions, Camera camera)
    {
        definitions = moduleDefinitions;
        sceneCamera = camera;
    }

    void Awake()
    {
        if (definitions == null || definitions.Length == 0)
        {
            Debug.LogError("ModularAssemblyLab has no module definitions.", this);
            enabled = false;
            return;
        }
        if (sceneCamera == null)
            sceneCamera = Camera.main;
        bool useAirBuildExperience =
            UnityPlanet.ModularAssembly.ModularLabSceneProfile
                .AllowsBuildExperience(gameObject.scene);
        BuildEnvironment(!useAirBuildExperience);
        if (EventSystem.current == null)
        {
            new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
        }

        GameObject ship = new GameObject("GridShip");
        ship.transform.position = Vector3.zero;
        var body = ship.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.drag = 0f;
        body.angularDrag = 0f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        Transform parts = new GameObject("Parts").transform;
        parts.SetParent(ship.transform, false);
        Transform core = new GameObject("CoreVisual").transform;
        core.SetParent(ship.transform, false);
        ShipAssembly assembly = ship.AddComponent<ShipAssembly>();
        assembly.Configure(body, parts, null, 1000f);
        var model = new GridAssemblyModel(definitions);
        GridAssemblyPresenter presenter = ship.AddComponent<GridAssemblyPresenter>();
        presenter.Initialize(model, assembly, core);

        GridLabCameraController cameraController = gameObject.AddComponent<GridLabCameraController>();
        cameraController.Initialize(sceneCamera, ship.transform);
        GridFlightBridge flight = ship.AddComponent<GridFlightBridge>();
        flight.Initialize(body, assembly, cameraController);

        GameObject targetObject = new GameObject("ModuleTarget");
        targetObject.transform.position = new Vector3(0f, 0f, 80f);
        GridTargetController target = targetObject.AddComponent<GridTargetController>();
        target.Initialize(definitions);

        UnityPlanet.ModularAssembly.WeaponSystemCoordinator weapons =
            ship.AddComponent<
                UnityPlanet.ModularAssembly.WeaponSystemCoordinator>();
        weapons.Initialize(
            presenter,
            flight,
            cameraController,
            model,
            target,
            sceneCamera);

        ModularAssemblyLabController controller = gameObject.AddComponent<ModularAssemblyLabController>();
        controller.Initialize(model, presenter, flight, cameraController, target, sceneCamera, ship.transform);

        // The current air-build experience supplies its own hangar and UI.
        // Creating the legacy view first lets it flash on screen whenever the
        // catalog or saved ship takes more than one frame to initialize.
        if (useAirBuildExperience)
            return;

        GameObject uiObject = new GameObject("ModularAssemblyUI", typeof(RectTransform));
        ModularAssemblyLabUI ui = uiObject.AddComponent<ModularAssemblyLabUI>();
        ui.Initialize(controller, definitions);
        controller.SetUI(ui);
    }

    void BuildEnvironment(bool createLegacyGeometry)
    {
        RenderSettings.ambientLight = new Color(0.08f, 0.12f, 0.18f);
        RenderSettings.ambientIntensity = 1.1f;
        sceneCamera.clearFlags = CameraClearFlags.SolidColor;
        sceneCamera.backgroundColor = new Color(0.003f, 0.008f, 0.018f);

        if (!createLegacyGeometry)
            return;

        CreatePlatform(new Vector3(0f, -3.6f, 0f), new Vector3(22f, 0.5f, 22f), new Color(0.045f, 0.08f, 0.10f));
        CreatePlatform(new Vector3(0f, -3.25f, 0f), new Vector3(9f, 0.18f, 9f), new Color(0.08f, 0.28f, 0.35f));
        for (int index = 0; index < 12; index++)
        {
            float angle = index * 30f * Mathf.Deg2Rad;
            Vector3 position = new Vector3(Mathf.Cos(angle) * 13f, -2.7f, Mathf.Sin(angle) * 13f);
            CreatePlatform(position, new Vector3(0.25f, 1.6f, 0.25f), new Color(0.12f, 0.42f, 0.48f));
        }
    }

    static void CreatePlatform(Vector3 position, Vector3 scale, Color color)
    {
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        target.name = "IndustrialTestPlatform";
        target.transform.position = position;
        target.transform.localScale = scale;
        Material material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        material.color = color;
        material.SetFloat("_Metallic", 0.75f);
        material.SetFloat("_Smoothness", 0.35f);
        target.GetComponent<Renderer>().sharedMaterial = material;
    }
}
