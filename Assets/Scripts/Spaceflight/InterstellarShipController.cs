using SpacecraftEditor;
using UnityEngine;
using UnityPlanet.ModularAssembly;

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
    bool modularMode;
    ModularInterstellarControlAdapter modularControls;
    RobocraftMotionCoordinator modularMotion;
    WeaponSystemCoordinator modularWeapons;
    VehicleStructureGraph modularStructure;
    InterstellarGridFlightSession modularSession;

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
            if (modularControls != null)
                modularControls.ControlsEnabled = initialized && value;
            if (modularWeapons != null)
                modularWeapons.ControlsEnabled = initialized && value;
        }
    }

    public bool CruiseActive => cruiseController != null && cruiseController.IsActive;
    public InterstellarCruiseState CruiseState => cruiseController == null
        ? InterstellarCruiseState.Inactive
        : cruiseController.State;
    public InterstellarWarpState WarpState => cruiseController == null
        ? InterstellarWarpState.Unlocked
        : cruiseController.WarpState;
    public InterstellarWarpCancelReason WarpCancelReason => cruiseController == null
        ? InterstellarWarpCancelReason.None
        : cruiseController.CancelReason;
    public float WarpProgress => cruiseController == null ? 0f : cruiseController.WarpProgress;
    public float WarpAlignmentError => cruiseController == null ? 0f : cruiseController.AlignmentError;
    public float WarpVisualIntensity => cruiseController == null ? 0f : cruiseController.WarpVisualIntensity;
    public float WarpExitDistance => cruiseController == null ? 0f : cruiseController.ExitDistance;
    public bool AutomaticLandingActive => cruiseController != null
        && cruiseController.AutomaticLandingRequested;
    public bool SurfaceEntryActive => cruiseController != null
        && cruiseController.IsSurfaceEntryActive;
    public float Speed => shipBody == null ? 0f : shipBody.velocity.magnitude;
    public bool StabilizationEnabled => modularMode
        ? modularMotion == null || modularMotion.CoreAssistMode != VehicleCoreAssistMode.Disabled
        : ifcsMotor == null || ifcsMotor.AssistMode != SpacecraftAssistMode.Decoupled;
    public SpacecraftAssistMode AssistMode => modularMode
        ? modularMotion != null && modularMotion.CoreAssistMode == VehicleCoreAssistMode.Disabled
            ? SpacecraftAssistMode.Decoupled
            : SpacecraftAssistMode.Coupled
        : ifcsMotor == null
            ? SpacecraftAssistMode.Coupled
            : ifcsMotor.AssistMode;
    public SpacecraftControlTelemetry Telemetry => modularMode
        ? modularControls == null ? default : modularControls.CaptureTelemetry()
        : ifcsMotor == null ? default : ifcsMotor.Telemetry;
    public KeyboardMouseFlightInput FlightInput => flightInput;
    public SpacecraftFlightCommand FlightCommand => modularMode
        ? modularControls == null ? default : modularControls.FlightCommand
        : flightInput == null ? default : flightInput.Command;
    public bool FreeLookHeld => modularMode
        ? modularControls != null && modularControls.FreeLookHeld
        : flightInput != null && flightInput.FreeLookHeld;
    public Rigidbody ShipBody => shipBody;
    public ShipAssembly Assembly => assembly;
    public SpacecraftWeaponSystem WeaponSystem => weaponSystem;
    public InterstellarCruiseController CruiseController => cruiseController;
    public bool IsModular => modularMode;
    public bool PersistIntegrity => !modularMode;
    public bool LinearControlEnabled
    {
        get => modularMode
            ? modularControls == null || modularControls.LinearControlEnabled
            : ifcsMotor == null || ifcsMotor.LinearControlEnabled;
        set
        {
            if (modularMode)
            {
                if (modularControls != null)
                    modularControls.LinearControlEnabled = value;
            }
            else if (ifcsMotor != null)
                ifcsMotor.LinearControlEnabled = value;
        }
    }
    public bool AngularControlEnabled
    {
        get => modularMode
            ? modularControls == null || modularControls.AngularControlEnabled
            : ifcsMotor == null || ifcsMotor.AngularControlEnabled;
        set
        {
            if (modularMode)
            {
                if (modularControls != null)
                    modularControls.AngularControlEnabled = value;
            }
            else if (ifcsMotor != null)
                ifcsMotor.AngularControlEnabled = value;
        }
    }
    public Vector3 VelocityReferenceWorld => modularMode
        ? modularControls == null ? Vector3.zero : modularControls.VelocityReferenceWorld
        : ifcsMotor == null ? Vector3.zero : ifcsMotor.VelocityReferenceWorld;
    public bool FlightControlForcesEnabled => modularMode
        ? modularControls != null && modularControls.ControlsEnabled
        : ifcsMotor != null && ifcsMotor.ControlsEnabled;
    public Bounds VisualBounds => CalculateVisualBounds();
    public InterstellarWeaponSnapshot WeaponSnapshot => CaptureWeaponSnapshot();

    public void ConfigureModular(ModularInterstellarControlAdapter controls)
    {
        modularMode = true;
        modularControls = controls;
    }

    public void BindModularRuntime(
        RobocraftMotionCoordinator motion,
        ModularInterstellarControlAdapter controls,
        WeaponSystemCoordinator weapons,
        VehicleStructureGraph structure,
        InterstellarGridFlightSession session)
    {
        modularMode = true;
        modularMotion = motion;
        modularControls = controls;
        modularWeapons = weapons;
        modularStructure = structure;
        modularSession = session;
    }

    public void ActivateModularFlight()
    {
        if (!modularMode)
            return;
        initialized = true;
        ControlsEnabled = controlsEnabled;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Awake()
    {
        shipBody = GetComponent<Rigidbody>();
        if (modularMode)
            ResolveModularReferences();
        else
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
        if (modularMode)
            return;
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

    void ResolveModularReferences()
    {
        if (cruiseController == null)
            cruiseController = GetComponent<InterstellarCruiseController>();
        if (modularControls == null)
            modularControls = GetComponent<ModularInterstellarControlAdapter>();
        if (GetComponent<SpaceCombatant>() == null)
        {
            SpaceCombatant combatant = gameObject.AddComponent<SpaceCombatant>();
            combatant.Configure(SpaceCombatFaction.Player, transform, true);
        }
    }

    public void SetExternalWorldVelocityTarget(Vector3 value)
    {
        if (modularMode)
            modularControls?.SetExternalWorldVelocityTarget(value);
        else
            ifcsMotor?.SetExternalWorldVelocityTarget(value);
    }

    public void SetExternalWorldAttitudeTarget(Quaternion value)
    {
        if (modularMode)
            modularControls?.SetExternalWorldAttitudeTarget(value);
        else
            ifcsMotor?.SetExternalWorldAttitudeTarget(value);
    }

    public void ClearExternalTargets()
    {
        if (modularMode)
            modularControls?.ClearExternalTargets();
        else
            ifcsMotor?.ClearExternalTargets();
    }

    public void ResetFlightController()
    {
        if (modularMode)
            modularControls?.ResetControllerState();
        else
            ifcsMotor?.ResetControllerState();
    }

    public void SetVelocityReference(Vector3 value)
    {
        if (modularMode)
            modularControls?.SetVelocityReference(value);
        else
            ifcsMotor?.SetVelocityReference(value);
    }

    public void ClearVelocityReference()
    {
        if (modularMode)
            modularControls?.ClearVelocityReference();
        else
            ifcsMotor?.ClearVelocityReference();
    }

    public void SetInputCaptureEnabled(bool value)
    {
        if (modularMode)
        {
            if (modularControls != null)
                modularControls.ControlsEnabled = initialized && value;
        }
        else if (flightInput != null)
            flightInput.CaptureEnabled = initialized && value;
    }

    public void SetFlightControlForcesEnabled(bool value)
    {
        if (modularMode)
        {
            if (modularControls != null)
                modularControls.ControlsEnabled = initialized && value;
        }
        else if (ifcsMotor != null)
            ifcsMotor.ControlsEnabled = initialized && value;
    }

    public void SetWeaponControlsEnabled(bool value)
    {
        if (modularMode)
        {
            if (modularWeapons != null)
                modularWeapons.ControlsEnabled = initialized && value;
        }
        else
        {
            if (weaponSystem != null)
                weaponSystem.ControlsEnabled = initialized && value;
            if (weaponInput != null)
                weaponInput.CaptureEnabled = initialized && value;
        }
    }

    InterstellarWeaponSnapshot CaptureWeaponSnapshot()
    {
        if (!modularMode)
        {
            if (weaponSystem == null)
                return new InterstellarWeaponSnapshot();
            ISpaceWeaponTarget target = weaponSystem.CurrentTarget;
            return new InterstellarWeaponSnapshot
            {
                available = true,
                group = weaponSystem.SelectedGroup,
                ammunition = weaponSystem.SelectedAmmunition,
                ammunitionCapacity = weaponSystem.SelectedAmmunitionCapacity,
                capacitorRatio = weaponSystem.CapacitorRatio,
                heatRatio = weaponSystem.SelectedHeat,
                groupOneAvailable = weaponSystem.GroupOneAvailable,
                groupTwoAvailable = weaponSystem.GroupTwoAvailable,
                groupHasGimbal = weaponSystem.SelectedGroupHasGimbal,
                targetLockEnabled = weaponSystem.TargetLockEnabled,
                hasTargetLock = weaponSystem.HasTargetLock,
                mountLabel = weaponSystem.SelectedMountLabel,
                targetTransform = target?.TargetTransform,
                targetAimPosition = target == null ? Vector3.zero : target.AimPosition,
                targetKind = weaponSystem.CurrentTargetKind,
                currentTarget = target
            };
        }

        if (modularWeapons == null)
            return new InterstellarWeaponSnapshot();
        int group = modularWeapons.ActiveGroup;
        int ammunition = 0;
        int capacity = 0;
        float heat = 0f;
        int heatCount = 0;
        bool groupOne = false;
        bool groupTwo = false;
        foreach (WeaponRuntime weapon in modularWeapons.Weapons)
        {
            if (weapon == null || weapon.Profile == null)
                continue;
            groupOne |= weapon.Group == 1;
            groupTwo |= weapon.Group == 2;
            if (weapon.Group != group)
                continue;
            if (weapon.Profile.ammunition > 0)
            {
                ammunition += weapon.Ammunition;
                capacity += weapon.Profile.ammunition;
            }
            heat += weapon.Heat;
            heatCount++;
        }
        Transform locked = modularWeapons.LockedTarget;
        ISpaceWeaponTarget targetInfo = FindSpaceTarget(locked);
        return new InterstellarWeaponSnapshot
        {
            available = modularWeapons.Weapons.Count > 0,
            group = group,
            ammunition = ammunition,
            ammunitionCapacity = capacity,
            capacitorRatio = modularWeapons.EnergyBus == null
                ? 0f
                : modularWeapons.EnergyBus.Fraction,
            heatRatio = heatCount == 0 ? 0f : heat / heatCount,
            groupOneAvailable = groupOne,
            groupTwoAvailable = groupTwo,
            groupHasGimbal = true,
            targetLockEnabled = true,
            hasTargetLock = locked != null,
            mountLabel = modularWeapons.IsUsingBuiltInWeapon
                ? "CORE DEFENSE"
                : "MODULAR",
            targetTransform = locked,
            targetAimPosition = targetInfo == null
                ? locked == null ? Vector3.zero : locked.position
                : targetInfo.AimPosition,
            targetKind = targetInfo == null
                ? locked == null ? SpaceWeaponTargetKind.None : SpaceWeaponTargetKind.Combatant
                : targetInfo.TargetKind,
            currentTarget = targetInfo
        };
    }

    static ISpaceWeaponTarget FindSpaceTarget(Transform candidate)
    {
        if (candidate == null)
            return null;
        MonoBehaviour[] components = candidate.GetComponentsInParent<MonoBehaviour>(true);
        for (int index = 0; index < components.Length; index++)
            if (components[index] is ISpaceWeaponTarget target)
                return target;
        return null;
    }

    Bounds CalculateVisualBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Bounds bounds = new Bounds(transform.position, new Vector3(3f, 2.2f, 6f));
        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (renderer == null)
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
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
        if (modularControls != null)
            modularControls.ControlsEnabled = false;
        if (modularWeapons != null)
            modularWeapons.ControlsEnabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
