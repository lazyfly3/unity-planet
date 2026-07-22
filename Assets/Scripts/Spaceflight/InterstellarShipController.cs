using SpacecraftEditor;
using UnityEngine;

[DefaultExecutionOrder(-500)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class InterstellarShipController : MonoBehaviour
{
    [Header("Assembly")]
    [SerializeField] ShipAssembly assembly;
    [SerializeField] ShipHullController hullController;
    [SerializeField] HullCatalog hullCatalog;
    [SerializeField] PartCatalog partCatalog;
    [SerializeField] GalaxySpacecraftBlueprintStore blueprintStore;

    [Header("IFCS")]
    [SerializeField] KeyboardMouseFlightInput flightInput;
    [SerializeField] SpacecraftIfcsMotor ifcsMotor;
    [SerializeField] InterstellarCruiseController cruiseController;
    [SerializeField] SpacecraftWeaponSystem weaponSystem;
    [SerializeField] PlayerSpacecraftWeaponInput weaponInput;

    Rigidbody shipBody;
    bool controlsEnabled = true;
    bool initialized;

    public bool ControlsEnabled
    {
        get => controlsEnabled;
        set
        {
            controlsEnabled = value;
            if (ifcsMotor != null)
                ifcsMotor.ControlsEnabled = initialized && value;
            if (flightInput != null)
                flightInput.CaptureEnabled = initialized && value;
            if (cruiseController != null)
                cruiseController.ControlsEnabled = initialized && value;
            if (weaponSystem != null)
                weaponSystem.ControlsEnabled = initialized && value;
            if (weaponInput != null)
                weaponInput.CaptureEnabled = initialized && value;
        }
    }

    public bool CruiseActive => cruiseController != null && cruiseController.IsActive;
    public InterstellarCruiseState CruiseState => cruiseController == null
        ? InterstellarCruiseState.Inactive
        : cruiseController.State;
    public float Speed => shipBody == null ? 0f : shipBody.velocity.magnitude;
    public bool StabilizationEnabled => ifcsMotor == null
        || ifcsMotor.AssistMode != SpacecraftAssistMode.Decoupled;
    public SpacecraftAssistMode AssistMode => ifcsMotor == null
        ? SpacecraftAssistMode.Coupled
        : ifcsMotor.AssistMode;
    public SpacecraftControlTelemetry Telemetry => ifcsMotor == null ? default : ifcsMotor.Telemetry;
    public KeyboardMouseFlightInput FlightInput => flightInput;
    public Rigidbody ShipBody => shipBody;
    public ShipAssembly Assembly => assembly;
    public SpacecraftWeaponSystem WeaponSystem => weaponSystem;

    void Awake()
    {
        shipBody = GetComponent<Rigidbody>();
        ResolveReferences();
        shipBody.useGravity = false;
        shipBody.drag = 0f;
        shipBody.angularDrag = 0f;
        shipBody.isKinematic = false;
        shipBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        shipBody.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void Start()
    {
        LoadBlueprint();
        if (ifcsMotor != null)
        {
            ifcsMotor.Configure(shipBody, assembly, hullController, flightInput, false);
            ifcsMotor.SetAssistMode(SpacecraftAssistMode.Coupled);
        }
        initialized = true;
        ControlsEnabled = controlsEnabled;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (!initialized || !controlsEnabled)
            return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void ResolveReferences()
    {
        if (assembly == null)
            assembly = GetComponent<ShipAssembly>() ?? FindObjectOfType<ShipAssembly>();
        if (hullController == null)
            hullController = GetComponentInChildren<ShipHullController>(true) ?? FindObjectOfType<ShipHullController>();
        if (hullCatalog == null)
            hullCatalog = FindObjectOfType<HullCatalog>();
        if (partCatalog == null)
            partCatalog = FindObjectOfType<PartCatalog>();
        if (blueprintStore == null)
            blueprintStore = FindObjectOfType<GalaxySpacecraftBlueprintStore>();
        if (flightInput == null)
            flightInput = GetComponent<KeyboardMouseFlightInput>();
        if (ifcsMotor == null)
            ifcsMotor = GetComponent<SpacecraftIfcsMotor>();
        if (cruiseController == null)
            cruiseController = GetComponent<InterstellarCruiseController>();
        if (weaponSystem == null)
            weaponSystem = GetComponent<SpacecraftWeaponSystem>() ?? gameObject.AddComponent<SpacecraftWeaponSystem>();
        if (weaponInput == null)
            weaponInput = GetComponent<PlayerSpacecraftWeaponInput>() ?? gameObject.AddComponent<PlayerSpacecraftWeaponInput>();
        if (GetComponent<SpaceCombatant>() == null)
        {
            SpaceCombatant combatant = gameObject.AddComponent<SpaceCombatant>();
            combatant.Configure(SpaceCombatFaction.Player, transform, true);
        }
        if (GetComponent<SpacecraftDamageReceiver>() != null && GetComponent<PlayerSpacecraftDamageLifecycle>() == null)
            gameObject.AddComponent<PlayerSpacecraftDamageLifecycle>();
        weaponSystem.SetCommandSource(weaponInput);
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            weaponSystem.SetTargetingView(mainCamera.transform);
    }

    void LoadBlueprint()
    {
        if (assembly == null || hullController == null || hullCatalog == null)
        {
            Debug.LogError("InterstellarShipController: the workshop ship assembly is incomplete.", this);
            return;
        }

        SpacecraftBlueprintData blueprint = null;
        if (blueprintStore != null)
            blueprintStore.TryLoad(out blueprint);
        ShipHullDefinition hull = blueprint == null ? null : hullCatalog.Find(blueprint.hullId);
        if (hull == null)
            hull = hullCatalog.DefaultDefinition;
        if (hull == null || !hullController.ApplyHull(hull))
        {
            Debug.LogError("InterstellarShipController: no valid spacecraft hull is available.", this);
            return;
        }
        if (!string.IsNullOrEmpty(blueprint?.hullMaterialId))
            hullController.ApplyPaint(blueprint.hullMaterialId);

        assembly.Configure(shipBody, assembly.PartsRoot, partCatalog, hull.BaseMass);
        assembly.RestoreStates(blueprint?.parts);
        if (assembly.Parts.Count == 0)
            AddFallbackThrusters(hull);
        // Re-scan after instantiation so the IFCS always receives the final live
        // thruster set, including scene-authored and restored blueprint parts.
        assembly.Configure(shipBody, assembly.PartsRoot, partCatalog, hull.BaseMass);
    }

    void AddFallbackThrusters(ShipHullDefinition hull)
    {
        if (partCatalog == null || partCatalog.Definitions == null || partCatalog.Definitions.Count == 0)
            return;
        ShipPartDefinition thruster = partCatalog.Find("thruster.medium");
        if (thruster == null || thruster.Category != SpacecraftPartCategory.Thruster)
        {
            for (int index = 0; index < partCatalog.Definitions.Count; index++)
            {
                ShipPartDefinition candidate = partCatalog.Definitions[index];
                if (candidate != null && candidate.Category == SpacecraftPartCategory.Thruster)
                {
                    thruster = candidate;
                    break;
                }
            }
        }
        if (thruster == null)
            return;
        float halfWidth = Mathf.Max(0.5f, hull.Dimensions.x * 0.27f);
        float rear = Mathf.Max(0.8f, hull.Dimensions.z * 0.42f);
        Quaternion exhaustRearward = Quaternion.Euler(0f, 180f, 0f);
        assembly.AddPart(thruster, new Vector3(-halfWidth, 0f, rear), exhaustRearward, 1f, activationKey: KeyCode.W);
        assembly.AddPart(thruster, new Vector3(halfWidth, 0f, rear), exhaustRearward, 1f, activationKey: KeyCode.W);
    }

    void OnDisable()
    {
        if (ifcsMotor != null)
            ifcsMotor.ControlsEnabled = false;
        if (cruiseController != null)
            cruiseController.ControlsEnabled = false;
        if (weaponSystem != null)
            weaponSystem.ControlsEnabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
