using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ModularAssembly;
using SpacecraftEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.ModularAssembly;
using UnityPlanet.SpaceStation;

public static class ModularSpaceLaunchStatus
{
    static string pendingMessage;

    public static void Set(string message)
    {
        pendingMessage = message ?? string.Empty;
    }

    public static bool TryConsume(out string message)
    {
        message = pendingMessage;
        pendingMessage = null;
        return !string.IsNullOrEmpty(message);
    }
}

public sealed class InterstellarWeaponSnapshot
{
    public bool available;
    public int group;
    public int ammunition;
    public int ammunitionCapacity;
    public float capacitorRatio;
    public float heatRatio;
    public bool groupOneAvailable;
    public bool groupTwoAvailable;
    public bool groupHasGimbal;
    public bool targetLockEnabled;
    public bool hasTargetLock;
    public string mountLabel;
    public Transform targetTransform;
    public Vector3 targetAimPosition;
    public SpaceWeaponTargetKind targetKind;
    public ISpaceWeaponTarget currentTarget;

    public int SelectedGroup => group;
    public int SelectedAmmunition => ammunition;
    public int SelectedAmmunitionCapacity => ammunitionCapacity;
    public float CapacitorRatio => capacitorRatio;
    public float SelectedHeat => heatRatio;
    public bool GroupOneAvailable => groupOneAvailable;
    public bool GroupTwoAvailable => groupTwoAvailable;
    public bool SelectedGroupHasGimbal => groupHasGimbal;
    public bool TargetLockEnabled => targetLockEnabled;
    public bool HasTargetLock => hasTargetLock;
    public string SelectedMountLabel => mountLabel;
    public SpaceWeaponTargetKind CurrentTargetKind => targetKind;
    public ISpaceWeaponTarget CurrentTarget => currentTarget;

    public bool OwnsTarget(Transform candidate)
    {
        return targetTransform != null && candidate != null &&
               (candidate == targetTransform ||
                candidate.IsChildOf(targetTransform) ||
                targetTransform.IsChildOf(candidate));
    }
}

public sealed class InterstellarGridFlightSession : MonoBehaviour,
    IGridFlightSession
{
    public GridFlightState State { get; private set; } =
        GridFlightState.LoadingTerrain;
    public bool IsFlying => State == GridFlightState.Flight;
    public event Action<GridFlightState, string> StateChanged;

    public void BeginSession()
    {
        State = GridFlightState.Flight;
        StateChanged?.Invoke(State, "模块飞船已进入真空航行。 ");
    }

    public void ExitFlight()
    {
        State = GridFlightState.Build;
        StateChanged?.Invoke(State, "模块太空航行已结束。 ");
    }
}

[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public sealed class ModularInterstellarControlAdapter :
    MonoBehaviour,
    IRobocraftPilotAimSource
{
    const int PointerWarmupFrames = 2;
    const float StandardYawSensitivity = 2.2f;
    const float StandardPitchSensitivity = 1.8f;
    const float StandardPitchLimit = 55f;
    const float ArcadeYawSensitivity = 2.35f;
    const float ArcadePitchSensitivity = 2.05f;
    const float ArcadePitchLimit = 80f;

    Rigidbody body;
    RobocraftMotionCoordinator motion;
    bool controlsEnabled;
    bool linearControlEnabled = true;
    bool angularControlEnabled = true;
    bool hasVelocityTarget;
    bool hasAttitudeTarget;
    Vector3 velocityTargetWorld;
    Quaternion attitudeTargetWorld = Quaternion.identity;
    Vector3 velocityReferenceWorld;
    float pilotAimYaw;
    float pilotAimPitch;
    bool pilotAimInitialized;
    bool pointerInputWasReady;
    int pointerWarmupFrames;

    public bool ControlsEnabled
    {
        get => controlsEnabled;
        set
        {
            bool changed = controlsEnabled != value;
            controlsEnabled = value;
            if (motion != null)
                motion.ControlsEnabled = value;
            if (!value)
            {
                motion?.ClearInjectedControl();
                pointerInputWasReady = false;
                pointerWarmupFrames = 0;
            }
            else if (changed)
            {
                ResetPilotAimToBody();
                pointerInputWasReady = false;
                pointerWarmupFrames = PointerWarmupFrames;
            }
        }
    }

    public bool LinearControlEnabled
    {
        get => linearControlEnabled;
        set => linearControlEnabled = value;
    }

    public bool AngularControlEnabled
    {
        get => angularControlEnabled;
        set
        {
            if (angularControlEnabled == value)
                return;
            angularControlEnabled = value;
            pointerInputWasReady = false;
            if (value)
                ResetPilotAimToBody();
        }
    }

    public Vector3 VelocityReferenceWorld => velocityReferenceWorld;
    public bool ArcadeAssistActive =>
        motion != null &&
        motion.CoreAssistMode == VehicleCoreAssistMode.Training;
    public bool FreeLookHeld => Input.GetKey(KeyCode.LeftAlt) ||
                                Input.GetKey(KeyCode.RightAlt);

    public SpacecraftFlightCommand FlightCommand =>
        new SpacecraftFlightCommand
        {
            translation = new Vector3(
                Axis(KeyCode.D, KeyCode.A),
                KeyboardMouseFlightInput.ResolveVerticalAxis(
                    Input.GetKey(KeyCode.Space),
                    Input.GetKey(KeyCode.LeftControl),
                    Input.GetKey(KeyCode.RightControl)),
                Axis(KeyCode.W, KeyCode.S)),
            roll = Axis(KeyCode.E, KeyCode.Q),
            brake = Input.GetKey(KeyCode.X),
            boost = Input.GetKey(KeyCode.LeftShift) ||
                    Input.GetKey(KeyCode.RightShift)
        };

    public void Configure(
        Rigidbody targetBody,
        RobocraftMotionCoordinator coordinator)
    {
        body = targetBody;
        motion = coordinator;
        controlsEnabled = false;
        ResetPilotAimToBody();
        pointerInputWasReady = false;
        pointerWarmupFrames = 0;
        if (motion != null)
            motion.ControlsEnabled = false;
    }

    public void SetExternalWorldVelocityTarget(Vector3 value)
    {
        velocityTargetWorld = value;
        hasVelocityTarget = true;
    }

    public void SetExternalWorldAttitudeTarget(Quaternion value)
    {
        attitudeTargetWorld = value;
        hasAttitudeTarget = true;
    }

    public void ClearExternalTargets()
    {
        hasVelocityTarget = false;
        hasAttitudeTarget = false;
        motion?.ClearInjectedControl();
        ResetPilotAimToBody();
        pointerInputWasReady = false;
        pointerWarmupFrames = PointerWarmupFrames;
    }

    public void ResetControllerState()
    {
        ClearExternalTargets();
    }

    public void SetVelocityReference(Vector3 value)
    {
        velocityReferenceWorld = value;
    }

    public void ClearVelocityReference()
    {
        velocityReferenceWorld = Vector3.zero;
    }

    public bool TryGetPilotAim(out Vector3 worldForward)
    {
        worldForward = pilotAimInitialized
            ? Quaternion.Euler(pilotAimPitch, pilotAimYaw, 0f) *
              Vector3.forward
            : transform.forward;
        return controlsEnabled &&
               angularControlEnabled &&
               !hasVelocityTarget &&
               !hasAttitudeTarget &&
               worldForward.sqrMagnitude > 0.0001f;
    }

    public SpacecraftControlTelemetry CaptureTelemetry()
    {
        if (body == null || motion == null)
            return default;
        RobocraftTelemetry source = motion.Telemetry;
        Transform frame = body.transform;
        Vector3 localPositiveForce = source.positiveForce;
        Vector3 localNegativeForce = source.negativeForce;
        return new SpacecraftControlTelemetry
        {
            localVelocity = frame.InverseTransformDirection(body.velocity),
            localAngularVelocity =
                frame.InverseTransformDirection(body.angularVelocity),
            requestedLocalForce = frame.InverseTransformDirection(
                source.requestedControlForceWorld),
            requestedLocalTorque = frame.InverseTransformDirection(
                source.requestedControlTorqueWorld),
            appliedLocalForce = frame.InverseTransformDirection(
                source.actualControlForceWorld),
            currentAcceleration = source.predictedLinearAccelerationWorld,
            shipMass = Mathf.Max(0.01f, body.mass),
            targetSpeed = hasVelocityTarget
                ? velocityTargetWorld.magnitude
                : body.velocity.magnitude,
            speedLimit = 10000f,
            controlAuthority = Mathf.Clamp01(source.controlClosureRatio),
            boostRatio = Input.GetKey(KeyCode.LeftShift) ||
                         Input.GetKey(KeyCode.RightShift) ? 1f : 0f,
            assistMode = motion.CoreAssistMode ==
                         VehicleCoreAssistMode.Disabled
                ? SpacecraftAssistMode.Decoupled
                : SpacecraftAssistMode.Coupled,
            directionalAuthority = new SpacecraftDirectionalAuthority
            {
                positiveForce = localPositiveForce,
                negativeForce = localNegativeForce,
                positiveAcceleration = source.positiveAcceleration,
                negativeAcceleration = source.negativeAcceleration,
                positiveTorque = source.angularAcceleration,
                negativeTorque = source.angularAcceleration
            },
            airDensity = 0f,
            airSpeed = body.velocity.magnitude,
            dynamicPressure = 0f,
            angleOfAttack = 0f,
            isStalling = false
        };
    }

    void FixedUpdate()
    {
        if (motion == null || body == null || !controlsEnabled)
            return;
        if (!hasVelocityTarget && !hasAttitudeTarget)
        {
            motion.ClearInjectedControl();
            return;
        }

        Vector3 up = Vector3.up;
        Vector3 aimForward = hasAttitudeTarget
            ? attitudeTargetWorld * Vector3.forward
            : velocityTargetWorld - body.velocity;
        if (aimForward.sqrMagnitude < 0.0001f)
            aimForward = transform.forward;
        aimForward.Normalize();

        var control = new RobocraftControlFrame
        {
            hasAimOverride = angularControlEnabled,
            aimForwardWorld = aimForward,
            freeLook = !angularControlEnabled
        };
        if (linearControlEnabled && hasVelocityTarget)
        {
            Vector3 error = velocityTargetWorld - body.velocity;
            float response = Mathf.Clamp01(error.magnitude / 18f);
            if (error.sqrMagnitude > 0.01f)
            {
                Vector3 direction = error.normalized;
                Vector3 planarForward = Vector3.ProjectOnPlane(
                    aimForward,
                    up).normalized;
                if (planarForward.sqrMagnitude < 0.0001f)
                    planarForward = Vector3.forward;
                Vector3 right = Vector3.Cross(up, planarForward).normalized;
                control.move = Vector2.ClampMagnitude(
                    new Vector2(
                        Vector3.Dot(direction, right),
                        Vector3.Dot(direction, planarForward)) * response,
                    1f);
                control.vertical = Mathf.Clamp(
                    Vector3.Dot(direction, up) * response,
                    -1f,
                    1f);
                control.boost = error.magnitude > 80f;
            }
        }
        motion.SetInjectedControl(control);
    }

    void Update()
    {
        if (!controlsEnabled ||
            !angularControlEnabled ||
            hasVelocityTarget ||
            hasAttitudeTarget)
        {
            pointerInputWasReady = false;
            return;
        }

        if (!pilotAimInitialized)
            ResetPilotAimToBody();

        bool pointerInputReady =
            Application.isFocused &&
            Cursor.lockState == CursorLockMode.Locked;
        if (!pointerInputReady)
        {
            pointerInputWasReady = false;
            return;
        }
        if (!pointerInputWasReady)
        {
            pointerInputWasReady = true;
            pointerWarmupFrames = PointerWarmupFrames;
            return;
        }
        if (pointerWarmupFrames > 0)
        {
            pointerWarmupFrames--;
            return;
        }
        if (FreeLookHeld)
            return;

        float yawSensitivity = ArcadeAssistActive
            ? ArcadeYawSensitivity
            : StandardYawSensitivity;
        float pitchSensitivity = ArcadeAssistActive
            ? ArcadePitchSensitivity
            : StandardPitchSensitivity;
        float pitchLimit = ArcadeAssistActive
            ? ArcadePitchLimit
            : StandardPitchLimit;
        pilotAimYaw += Input.GetAxisRaw("Mouse X") * yawSensitivity;
        pilotAimPitch = Mathf.Clamp(
            pilotAimPitch - Input.GetAxisRaw("Mouse Y") * pitchSensitivity,
            -pitchLimit,
            pitchLimit);
        pilotAimYaw = Mathf.Repeat(pilotAimYaw + 180f, 360f) - 180f;
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            return;
        pointerInputWasReady = false;
        pointerWarmupFrames = PointerWarmupFrames;
    }

    void ResetPilotAimToBody()
    {
        Vector3 forward = body != null
            ? body.transform.forward
            : transform.forward;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();
        pilotAimYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        float pitchLimit = ArcadeAssistActive
            ? ArcadePitchLimit
            : StandardPitchLimit;
        pilotAimPitch = Mathf.Clamp(
            -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) *
            Mathf.Rad2Deg,
            -pitchLimit,
            pitchLimit);
        pilotAimInitialized = true;
    }

    static float Axis(KeyCode positive, KeyCode negative)
    {
        return (Input.GetKey(positive) ? 1f : 0f) -
               (Input.GetKey(negative) ? 1f : 0f);
    }
}

[DisallowMultipleComponent]
public sealed class ModularInterstellarDamageBridge : MonoBehaviour
{
    readonly Dictionary<string, float> maximumById =
        new Dictionary<string, float>(StringComparer.Ordinal);
    readonly Dictionary<string, float> currentById =
        new Dictionary<string, float>(StringComparer.Ordinal);
    VehicleStructureGraph graph;
    SpacecraftDamageReceiver aggregate;
    Rigidbody body;
    float totalMaximum;

    public void Initialize(
        GridAssemblyModel model,
        VehicleStructureGraph structure,
        SpacecraftDamageReceiver receiver,
        Rigidbody targetBody)
    {
        graph = structure;
        aggregate = receiver;
        body = targetBody;
        maximumById.Clear();
        currentById.Clear();
        totalMaximum = 0f;
        foreach (GridModuleRecord record in model.Records)
        {
            float maximum = Mathf.Max(1f, record.Definition.MaxIntegrity);
            maximumById[record.RuntimeId] = maximum;
            currentById[record.RuntimeId] = maximum;
            totalMaximum += maximum;
        }
        graph.ModuleDamaged += HandleModuleDamaged;
        graph.StructureChanged += HandleStructureChanged;
        graph.Destroyed += HandleDestroyed;
    }

    void HandleModuleDamaged(VehicleModuleDamageFeedback feedback)
    {
        if (maximumById.TryGetValue(feedback.RuntimeId, out float maximum))
            currentById[feedback.RuntimeId] = maximum * feedback.HealthRatio;
        if (feedback.Impulse.sqrMagnitude > 0.000001f && body != null)
        {
            VehicleExternalForces.ApplyImpulse(
                body,
                feedback.Impulse,
                feedback.HitPoint);
        }
        SynchronizeAggregate(new SpaceDamageInfo(
            Mathf.Max(0.01f, feedback.DamageAmount),
            feedback.HitPoint,
            feedback.Impulse,
            SpaceDamageType.Projectile,
            null));
    }

    void HandleStructureChanged(VehicleStructureDelta delta)
    {
        if (delta?.RemovedRuntimeIds != null)
        {
            foreach (string id in delta.RemovedRuntimeIds)
                if (currentById.ContainsKey(id))
                    currentById[id] = 0f;
        }
        SynchronizeAggregate(new SpaceDamageInfo(
            0.01f,
            delta == null ? transform.position : delta.HitPoint,
            delta == null ? Vector3.zero : delta.Impulse,
            SpaceDamageType.Projectile,
            null));
    }

    void HandleDestroyed()
    {
        foreach (string id in maximumById.Keys.ToArray())
            currentById[id] = 0f;
        SynchronizeAggregate(new SpaceDamageInfo(
            100f,
            transform.position,
            Vector3.zero,
            SpaceDamageType.Explosion,
            null));
    }

    void SynchronizeAggregate(SpaceDamageInfo cause)
    {
        if (aggregate == null || totalMaximum <= 0.01f)
            return;
        float desired = Mathf.Clamp(
            currentById.Values.Sum() / totalMaximum * 100f,
            0f,
            100f);
        float damage = aggregate.Integrity - desired;
        if (damage <= 0.0001f)
            return;
        aggregate.ApplyDamage(new SpaceDamageInfo(
            damage,
            cause.point,
            cause.impulse,
            cause.type,
            cause.source));
    }

    void OnDestroy()
    {
        if (graph == null)
            return;
        graph.ModuleDamaged -= HandleModuleDamaged;
        graph.StructureChanged -= HandleStructureChanged;
        graph.Destroyed -= HandleDestroyed;
    }
}

[DisallowMultipleComponent]
public sealed class ModularInterstellarDamageLifecycle : MonoBehaviour
{
    InterstellarShipController ship;
    VehicleStructureGraph graph;
    RobocraftMotionCoordinator motion;
    SpacecraftDamageReceiver aggregate;
    bool recovering;

    public void Initialize(
        InterstellarShipController controller,
        VehicleStructureGraph structure,
        RobocraftMotionCoordinator coordinator,
        SpacecraftDamageReceiver receiver)
    {
        ship = controller;
        graph = structure;
        motion = coordinator;
        aggregate = receiver;
        graph.Destroyed += HandleDestroyed;
        if (aggregate != null)
            aggregate.Destroyed += HandleAggregateDestroyed;
    }

    void HandleDestroyed()
    {
        if (recovering)
            return;
        recovering = true;
        if (ship != null)
            ship.ControlsEnabled = false;
        motion?.SetDamageDisabled(true);
        SpaceCombatVfxPool effects = SpaceCombatVfxPool.Instance;
        if (effects != null && effects.Catalog != null)
        {
            effects.PlayOneShot(
                effects.Catalog.ShipDestructionPrefab,
                transform.position,
                transform.rotation,
                1.6f,
                4f);
        }
        StartCoroutine(ReturnToLab());
    }

    void HandleAggregateDestroyed(SpaceDamageInfo damage)
    {
        HandleDestroyed();
    }

    IEnumerator ReturnToLab()
    {
        yield return new WaitForSecondsRealtime(1.5f);
        ModularSpaceLaunchStatus.Set(
            "模块飞船已摧毁；战损未写回，已恢复最后一次成功保存的设计。 ");
        SceneManager.LoadScene("ModularAssemblyLab", LoadSceneMode.Single);
    }

    void OnDestroy()
    {
        if (graph != null)
            graph.Destroyed -= HandleDestroyed;
        if (aggregate != null)
            aggregate.Destroyed -= HandleAggregateDestroyed;
    }
}

[DisallowMultipleComponent]
public sealed class InterstellarModularVehicleLoader : MonoBehaviour
{
    const string DefinitionResourcePath =
        "ModularAssembly/Definitions";

    GameObject modularRoot;
    GameObject legacyRoot;
    ModularContentService contentService;

    public InterstellarShipController ShipController { get; private set; }
    public Rigidbody Body { get; private set; }

    public void Prepare(GameObject currentLegacyRoot)
    {
        legacyRoot = currentLegacyRoot;
        if (legacyRoot != null)
            legacyRoot.SetActive(false);

        modularRoot = new GameObject("ModularInterstellarShip");
        modularRoot.SetActive(false);
        Body = modularRoot.AddComponent<Rigidbody>();
        Body.useGravity = false;
        Body.drag = 0f;
        Body.angularDrag = 0f;
        Body.isKinematic = true;
        Body.collisionDetectionMode =
            CollisionDetectionMode.ContinuousSpeculative;
        Body.interpolation = RigidbodyInterpolation.Interpolate;

        modularRoot.AddComponent<SpacecraftDamageReceiver>();
        ModularInterstellarControlAdapter controls =
            modularRoot.AddComponent<ModularInterstellarControlAdapter>();
        ShipController =
            modularRoot.AddComponent<InterstellarShipController>();
        modularRoot.AddComponent<InterstellarCruiseController>();
        ShipController.ConfigureModular(controls);
    }

    public IEnumerator Build(Action<bool, string> completed)
    {
        contentService = gameObject.AddComponent<ModularContentService>();
        yield return contentService.Initialize();
        if (contentService.Catalog == null || contentService.Catalog.Count == 0)
        {
            completed?.Invoke(
                false,
                "模块内容目录不可用：" +
                (contentService.LastError ?? "目录为空。"));
            yield break;
        }

        GridModuleDefinition[] baseDefinitions =
            Resources.LoadAll<GridModuleDefinition>(DefinitionResourcePath);
        if (baseDefinitions == null || baseDefinitions.Length == 0)
        {
            completed?.Invoke(false, "运行时模块定义资源缺失。 ");
            yield break;
        }

        var model = new GridAssemblyModel(baseDefinitions);
        IReadOnlyDictionary<string, ModularContentRecord> records =
            NeoXCatalogIntegration.RegisterDefinitions(
                model,
                contentService.Catalog);
        var store = new ModularBlueprintStore();
        ModularBlueprintData blueprint;
        string loadError = string.Empty;
        bool hasTransientBlueprint =
            SpaceStationFlowContext.TryTakePendingSpaceBlueprint(
                out blueprint);
        if (!hasTransientBlueprint &&
            !store.TryLoad(out blueprint, out loadError))
        {
            completed?.Invoke(
                false,
                string.IsNullOrEmpty(loadError)
                    ? "没有可用于太空航行的模块蓝图。 "
                    : loadError);
            yield break;
        }
        if (!model.RestoreBlueprint(blueprint, out string restoreError))
        {
            completed?.Invoke(false, "模块蓝图恢复失败：" + restoreError);
            yield break;
        }

        Transform parts = new GameObject("Parts").transform;
        parts.SetParent(modularRoot.transform, false);
        Transform core = new GameObject("CoreVisual").transform;
        core.SetParent(modularRoot.transform, false);
        ShipAssembly assembly = modularRoot.AddComponent<ShipAssembly>();
        assembly.Configure(Body, parts, null, 1000f);
        GridAssemblyPresenter presenter =
            modularRoot.AddComponent<GridAssemblyPresenter>();
        presenter.Initialize(model, assembly, core);

        VacuumEnvironmentProvider vacuum =
            modularRoot.AddComponent<VacuumEnvironmentProvider>();
        RobocraftMotionCoordinator motion =
            modularRoot.AddComponent<RobocraftMotionCoordinator>();
        motion.ConfigureExplicit(Body, assembly, model, presenter);
        motion.SetEnvironmentProvider(vacuum);
        ModularInterstellarControlAdapter controls =
            modularRoot.GetComponent<ModularInterstellarControlAdapter>();
        controls.Configure(Body, motion);

        InterstellarGridFlightSession session =
            modularRoot.AddComponent<InterstellarGridFlightSession>();
        NeoXCatalogIntegration integration =
            modularRoot.AddComponent<NeoXCatalogIntegration>();
        modularRoot.SetActive(true);
        integration.InitializeRuntime(contentService, presenter, records);

        WeaponSystemCoordinator weapons =
            modularRoot.AddComponent<WeaponSystemCoordinator>();
        weapons.Initialize(
            presenter,
            session,
            null,
            model,
            null,
            Camera.main);
        weapons.SetHudVisible(false);
        VehicleStructureGraph graph = weapons.StructureGraph;
        graph.SetAutomaticReturnToBuild(false);
        graph.SetDamageEnabled(true);

        SpacecraftDamageReceiver aggregate =
            modularRoot.GetComponent<SpacecraftDamageReceiver>();
        ModularInterstellarDamageBridge damageBridge =
            modularRoot.AddComponent<ModularInterstellarDamageBridge>();
        damageBridge.Initialize(model, graph, aggregate, Body);
        ModularInterstellarDamageLifecycle lifecycle =
            modularRoot.AddComponent<ModularInterstellarDamageLifecycle>();
        lifecycle.Initialize(ShipController, graph, motion, aggregate);
        ShipController.BindModularRuntime(
            motion,
            controls,
            weapons,
            graph,
            session);

        while (!integration.IsReady || contentService.IsLoadingAssets)
            yield return null;

        SetLayerRecursively(
            modularRoot,
            LayerMask.NameToLayer("SpacePhysicsBubble"));
        Body.isKinematic = false;
        Body.WakeUp();
        if (!motion.TryBeginFlight(
                true,
                out string physicsMessage))
        {
            completed?.Invoke(false, "RC3 真空物理检查失败：" + physicsMessage);
            yield break;
        }
        session.BeginSession();
        ShipController.ActivateModularFlight();
        if (legacyRoot != null)
            Destroy(legacyRoot);
        completed?.Invoke(true, physicsMessage);
    }

    public void AbortAndReturnToLab(string message)
    {
        if (modularRoot != null)
            Destroy(modularRoot);
        ModularSpaceLaunchStatus.Set(
            "进入太空失败，蓝图未被修改：" + message);
        SceneManager.LoadScene("ModularAssemblyLab", LoadSceneMode.Single);
    }

    static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null || layer < 0)
            return;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;
    }
}
