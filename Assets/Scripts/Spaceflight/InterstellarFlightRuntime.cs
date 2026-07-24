using System;
using SpacecraftEditor;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-700)]
[DisallowMultipleComponent]
public sealed class InterstellarFlightRuntime : MonoBehaviour
{
    const string KilometerLayerName = "SpaceKilometerView";
    const string PhysicsBubbleLayerName = "SpacePhysicsBubble";
    const string AstronomicalRootName = "AstronomicalRoot";
    const string AstronomicalCameraName = "AstronomicalCamera";

    [SerializeField] Rigidbody shipBody;
    [SerializeField] SpacecraftDamageReceiver damageReceiver;
    [SerializeField] SpacecraftIfcsMotor ifcsMotor;
    [SerializeField] InterstellarShipController shipController;
    [FormerlySerializedAs("floatingOriginThreshold")]
    [SerializeField, Min(250f)] float floatingOriginThresholdMeters = 2000f;
    [SerializeField, Min(10f)] float highSpeedRebaseThresholdMeters = 100f;
    [SerializeField, Min(100f)] float highSpeedEnterMetersPerSecond = 1000f;
    [SerializeField, Min(50f)] float highSpeedExitMetersPerSecond = 600f;
    [SerializeField, Min(0f)] float tacticalResumeDelaySeconds = 1f;
    [SerializeField, Min(1000f)] float astronomicalFarClipKilometers = 120000f;
    [SerializeField, Min(0.25f)] float saveInterval = 2f;

    UniversePosition universeOrigin;
    float nextSaveTime;
    float interactionEvaluationStartsAt;
    float tacticalResumeAt = -1f;
    bool initialized;
    bool warpCinematicOverride;
    bool tacticalPhysicsSyncPending;
    bool originalDetectCollisions = true;
    Transform astronomicalRoot;
    Camera astronomicalCamera;
    Camera localCamera;
    CameraClearFlags originalLocalClearFlags;
    int originalLocalCullingMask;
    bool presentationConfigured;

    public event Action<Vector3> OriginShifted;
    public event Action<DoubleVector3, DoubleVector3> UniverseRelocated;
    public event Action<UniversePosition, UniversePosition> UniverseAddressRelocated;
    public event Action<SpaceflightInteractionMode> InteractionModeChanged;
    public Rigidbody ShipBody => shipBody;
    public Transform AstronomicalRoot => astronomicalRoot;
    public Camera AstronomicalCamera => astronomicalCamera;
    public SpaceflightInteractionMode InteractionMode { get; private set; }
        = SpaceflightInteractionMode.TacticalPhysics;
    public bool LocalInteractionsEnabled
        => InteractionMode == SpaceflightInteractionMode.TacticalPhysics;
    public Vector3 ShipVelocityMetersPerSecond
        => shipBody == null ? Vector3.zero : shipBody.velocity;
    public Vector3 ShipVelocityKilometerUnitsPerSecond
        => SpaceKilometerScale.ToKilometerUnitsPerSecond(
            ShipVelocityMetersPerSecond);
    public Vector3 ShipRelativeVelocityMetersPerSecond
        => shipBody != null
            && ifcsMotor != null
            && ifcsMotor.Telemetry.shipMass > 0f
            ? shipBody.transform.TransformDirection(ifcsMotor.Telemetry.localVelocity)
            : ShipVelocityMetersPerSecond;
    public float HighSpeedEnterMetersPerSecond => highSpeedEnterMetersPerSecond;
    public float HighSpeedExitMetersPerSecond => highSpeedExitMetersPerSecond;
    public UniversePosition PhysicalUniverseOrigin => universeOrigin;
    public UniversePosition ShipPhysicalUniversePosition => universeOrigin.Add(new DoubleVector3(
        shipBody == null ? 0d : shipBody.position.x,
        shipBody == null ? 0d : shipBody.position.y,
        shipBody == null ? 0d : shipBody.position.z));
    public DoubleVector3 UniverseOrigin => universeOrigin.ToAbsoluteMeters();
    public DoubleVector3 ShipUniversePosition => ShipPhysicalUniversePosition.ToAbsoluteMeters();

    public Vector3 ToLocalPosition(UniversePosition universePosition)
    {
        DoubleVector3 offset = UniversePosition.Delta(universeOrigin, universePosition);
        return offset.ToVector3();
    }

    public Vector3 ToPhysicsBubblePosition(UniversePosition universePosition)
        => ToLocalPosition(universePosition);

    public Vector3 ToKilometerRenderPosition(UniversePosition universePosition)
    {
        DoubleVector3 offsetMeters = UniversePosition.Delta(
            ShipPhysicalUniversePosition,
            universePosition);
        return SpaceKilometerScale.ToKilometerUnits(offsetMeters);
    }

    public Vector3 ToKilometerRenderPosition(DoubleVector3 universePosition)
        => ToKilometerRenderPosition(
            UniversePosition.FromAbsoluteMeters(universePosition));

    public Vector3 ToLocalPosition(DoubleVector3 universePosition)
    {
        return ToLocalPosition(UniversePosition.FromAbsoluteMeters(universePosition));
    }

    public DoubleVector3 ToUniversePosition(Vector3 localPosition)
    {
        return ToUniverseAddress(localPosition).ToAbsoluteMeters();
    }

    public UniversePosition ToUniverseAddress(Vector3 localPosition)
        => universeOrigin.Add(new DoubleVector3(localPosition.x, localPosition.y, localPosition.z));

    void Awake()
    {
        RenderSettings.skybox = null;
        RenderSettings.fog = false;
        foreach (StarfieldEnvironment starfield in FindObjectsOfType<StarfieldEnvironment>())
            starfield.gameObject.SetActive(false);
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = Color.black;
        }

        if (shipBody == null)
            shipBody = FindObjectOfType<InterstellarShipController>()?.GetComponent<Rigidbody>();
        if (damageReceiver == null && shipBody != null)
            damageReceiver = shipBody.GetComponent<SpacecraftDamageReceiver>();
        if (ifcsMotor == null && shipBody != null)
            ifcsMotor = shipBody.GetComponent<SpacecraftIfcsMotor>();
        if (shipController == null && shipBody != null)
            shipController = shipBody.GetComponent<InterstellarShipController>();

        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        universeOrigin = manager != null && manager.IsInterstellarGalaxy
            ? manager.SavedUniversePosition
            : default;
        if (shipBody != null)
        {
            shipBody.position = Vector3.zero;
            if (PendingSurfaceDepartureContext.TryConsume(
                out Quaternion departureRotation,
                out Vector3 departureVelocity))
            {
                shipBody.rotation = departureRotation;
                shipBody.velocity = departureVelocity;
                shipBody.angularVelocity = Vector3.zero;
            }
            originalDetectCollisions = shipBody.detectCollisions;
        }
        highSpeedEnterMetersPerSecond = Mathf.Max(
            100f,
            highSpeedEnterMetersPerSecond);
        highSpeedExitMetersPerSecond = Mathf.Clamp(
            highSpeedExitMetersPerSecond,
            50f,
            highSpeedEnterMetersPerSecond);
        ConfigureDualScalePresentation();
        interactionEvaluationStartsAt = Time.unscaledTime + 1f;
        initialized = true;
    }

    void FixedUpdate()
    {
        if (!initialized || shipBody == null)
            return;

        UpdateInteractionMode();
        float rebaseThreshold = InteractionMode == SpaceflightInteractionMode.TacticalPhysics
            ? floatingOriginThresholdMeters
            : highSpeedRebaseThresholdMeters;
        if (shipBody.position.sqrMagnitude >= rebaseThreshold * rebaseThreshold)
            ShiftOrigin(shipBody.position);
    }

    void Update()
    {
        if (!initialized || !Input.GetKeyDown(KeyCode.M))
            return;
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (manager == null || !manager.IsInterstellarGalaxy)
            return;
        SaveState(true);
        manager.OpenGalaxyMap(null);
    }

    void LateUpdate()
    {
        if (!initialized || shipBody == null)
            return;

        if (Time.unscaledTime >= nextSaveTime)
        {
            SaveState(false);
            nextSaveTime = Time.unscaledTime + saveInterval;
        }
    }

    void ShiftOrigin(Vector3 localShift)
    {
        universeOrigin = universeOrigin.Add(new DoubleVector3(localShift.x, localShift.y, localShift.z));
        RigidbodyInterpolation interpolation = shipBody.interpolation;
        shipBody.interpolation = RigidbodyInterpolation.None;
        shipBody.position -= localShift;
        Physics.SyncTransforms();
        OriginShifted?.Invoke(localShift);
        shipBody.interpolation = interpolation;
    }

    public void SetWarpCinematic(bool active)
    {
        warpCinematicOverride = active;
        if (initialized)
            UpdateInteractionMode(true);
    }

    public void WarpToUniversePosition(
        DoubleVector3 destination,
        Vector3 exitVelocity,
        Quaternion exitRotation)
    {
        WarpToUniversePosition(
            UniversePosition.FromAbsoluteMeters(destination),
            exitVelocity,
            exitRotation);
    }

    public void WarpToUniversePosition(
        UniversePosition destination,
        Vector3 exitVelocity,
        Quaternion exitRotation)
    {
        if (!initialized || shipBody == null)
            return;

        DoubleVector3 previousPosition = ShipUniversePosition;
        UniversePosition previousAddress = ShipPhysicalUniversePosition;
        RigidbodyInterpolation interpolation = shipBody.interpolation;
        shipBody.interpolation = RigidbodyInterpolation.None;
        universeOrigin = destination;
        shipBody.position = Vector3.zero;
        shipBody.rotation = exitRotation;
        shipBody.velocity = exitVelocity;
        shipBody.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
        UniverseRelocated?.Invoke(previousPosition, destination.ToAbsoluteMeters());
        UniverseAddressRelocated?.Invoke(previousAddress, destination);
        shipBody.interpolation = interpolation;
        SaveState(true);
    }

    void UpdateInteractionMode(bool force = false)
    {
        if (shipBody == null)
            return;

        SpaceflightInteractionMode desired = InteractionMode;
        if (warpCinematicOverride)
        {
            desired = SpaceflightInteractionMode.WarpCinematic;
            tacticalResumeAt = -1f;
            tacticalPhysicsSyncPending = false;
        }
        else if (!force && Time.unscaledTime < interactionEvaluationStartsAt)
        {
            desired = SpaceflightInteractionMode.TacticalPhysics;
        }
        else
        {
            float relativeSpeed = ShipRelativeVelocityMetersPerSecond.magnitude;
            bool currentlyFast = InteractionMode != SpaceflightInteractionMode.TacticalPhysics;
            if (!currentlyFast && ShouldEnterHighSpeed(
                relativeSpeed,
                highSpeedEnterMetersPerSecond))
            {
                desired = SpaceflightInteractionMode.HighSpeedTravel;
                tacticalResumeAt = -1f;
                tacticalPhysicsSyncPending = false;
            }
            else if (currentlyFast)
            {
                if (!CanReturnToTactical(
                    relativeSpeed,
                    highSpeedExitMetersPerSecond))
                {
                    desired = SpaceflightInteractionMode.HighSpeedTravel;
                    tacticalResumeAt = -1f;
                    tacticalPhysicsSyncPending = false;
                }
                else
                {
                    if (tacticalResumeAt < 0f)
                        tacticalResumeAt = Time.unscaledTime + tacticalResumeDelaySeconds;
                    if (Time.unscaledTime < tacticalResumeAt)
                    {
                        desired = SpaceflightInteractionMode.HighSpeedTravel;
                    }
                    else if (!tacticalPhysicsSyncPending)
                    {
                        // Let transforms settle for one fixed step before
                        // collision callbacks and encounter spawning resume.
                        Physics.SyncTransforms();
                        tacticalPhysicsSyncPending = true;
                        desired = SpaceflightInteractionMode.HighSpeedTravel;
                    }
                    else
                    {
                        desired = SpaceflightInteractionMode.TacticalPhysics;
                    }
                }
            }
        }

        if (desired == InteractionMode)
            return;

        InteractionMode = desired;
        tacticalResumeAt = desired == SpaceflightInteractionMode.TacticalPhysics
            ? -1f
            : tacticalResumeAt;
        if (desired == SpaceflightInteractionMode.TacticalPhysics)
            tacticalPhysicsSyncPending = false;
        shipBody.detectCollisions = desired == SpaceflightInteractionMode.TacticalPhysics
            && originalDetectCollisions;
        if (desired == SpaceflightInteractionMode.TacticalPhysics)
            Physics.SyncTransforms();
        InteractionModeChanged?.Invoke(desired);
    }

    public static bool ShouldEnterHighSpeed(
        float relativeSpeedMetersPerSecond,
        float enterThresholdMetersPerSecond = 1000f)
        => relativeSpeedMetersPerSecond
            >= Mathf.Max(0f, enterThresholdMetersPerSecond);

    public static bool CanReturnToTactical(
        float relativeSpeedMetersPerSecond,
        float exitThresholdMetersPerSecond = 600f)
        => relativeSpeedMetersPerSecond
            <= Mathf.Max(0f, exitThresholdMetersPerSecond);

    void ConfigureDualScalePresentation()
    {
        if (!string.Equals(gameObject.scene.name, "InterstellarFlight", StringComparison.Ordinal))
            return;

        int kilometerLayer = LayerMask.NameToLayer(KilometerLayerName);
        if (kilometerLayer < 0)
        {
            Debug.LogError(
                $"InterstellarFlightRuntime: required layer '{KilometerLayerName}' is missing.",
                this);
            return;
        }

        GameObject rootObject = FindInRuntimeScene(AstronomicalRootName);
        if (rootObject == null)
        {
            rootObject = new GameObject(AstronomicalRootName);
            SceneManager.MoveGameObjectToScene(rootObject, gameObject.scene);
        }
        astronomicalRoot = rootObject.transform;
        astronomicalRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        astronomicalRoot.localScale = Vector3.one;
        if (rootObject.GetComponent<SpaceKilometerScaleValidator>() == null)
            rootObject.AddComponent<SpaceKilometerScaleValidator>();
        SetLayerRecursively(rootObject, kilometerLayer);

        Transform existingPlanetRoot = FindInRuntimeScene("PlanetRuntimeRoot")?.transform;
        if (existingPlanetRoot != null && existingPlanetRoot != astronomicalRoot)
        {
            existingPlanetRoot.SetParent(astronomicalRoot, false);
            existingPlanetRoot.localPosition = Vector3.zero;
            existingPlanetRoot.localRotation = Quaternion.identity;
            existingPlanetRoot.localScale = Vector3.one;
            SetLayerRecursively(existingPlanetRoot.gameObject, kilometerLayer);
        }

        GameObject physicsBubble = FindInRuntimeScene("SpacePhysicsBubble");
        if (physicsBubble == null)
        {
            physicsBubble = new GameObject("SpacePhysicsBubble");
            SceneManager.MoveGameObjectToScene(physicsBubble, gameObject.scene);
        }
        physicsBubble.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        physicsBubble.transform.localScale = Vector3.one;
        int physicsLayer = LayerMask.NameToLayer(PhysicsBubbleLayerName);
        if (physicsLayer >= 0)
            physicsBubble.layer = physicsLayer;

        localCamera = Camera.main;
        if (localCamera == null)
            return;

        originalLocalClearFlags = localCamera.clearFlags;
        originalLocalCullingMask = localCamera.cullingMask;
        localCamera.cullingMask &= ~(1 << kilometerLayer);
        localCamera.clearFlags = CameraClearFlags.Depth;

        GameObject cameraObject = FindInRuntimeScene(AstronomicalCameraName);
        if (cameraObject == null)
        {
            cameraObject = new GameObject(AstronomicalCameraName);
            SceneManager.MoveGameObjectToScene(cameraObject, gameObject.scene);
        }
        astronomicalCamera = cameraObject.GetComponent<Camera>();
        if (astronomicalCamera == null)
            astronomicalCamera = cameraObject.AddComponent<Camera>();
        astronomicalCamera.CopyFrom(localCamera);
        astronomicalCamera.tag = "Untagged";
        astronomicalCamera.cullingMask = 1 << kilometerLayer;
        astronomicalCamera.clearFlags = CameraClearFlags.SolidColor;
        astronomicalCamera.backgroundColor = Color.black;
        astronomicalCamera.depth = localCamera.depth - 10f;
        astronomicalCamera.nearClipPlane = 0.05f;
        astronomicalCamera.farClipPlane = Mathf.Max(
            1000f,
            astronomicalFarClipKilometers);
        astronomicalCamera.enabled = true;

        AstronomicalCameraSynchronizer synchronizer =
            cameraObject.GetComponent<AstronomicalCameraSynchronizer>();
        if (synchronizer == null)
            synchronizer = cameraObject.AddComponent<AstronomicalCameraSynchronizer>();
        synchronizer.Configure(astronomicalCamera, localCamera, shipBody);
        presentationConfigured = true;
    }

    GameObject FindInRuntimeScene(string objectName)
    {
        if (string.IsNullOrEmpty(objectName) || !gameObject.scene.IsValid())
            return null;
        GameObject[] roots = gameObject.scene.GetRootGameObjects();
        for (int index = 0; index < roots.Length; index++)
        {
            GameObject match = FindInHierarchy(roots[index], objectName);
            if (match != null)
                return match;
        }
        return null;
    }

    static GameObject FindInHierarchy(GameObject candidate, string objectName)
    {
        if (candidate == null)
            return null;
        if (string.Equals(candidate.name, objectName, StringComparison.Ordinal))
            return candidate;
        Transform root = candidate.transform;
        for (int index = 0; index < root.childCount; index++)
        {
            GameObject match = FindInHierarchy(root.GetChild(index).gameObject, objectName);
            if (match != null)
                return match;
        }
        return null;
    }

    static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null || layer < 0)
            return;
        root.layer = layer;
        Transform transform = root.transform;
        for (int index = 0; index < transform.childCount; index++)
            SetLayerRecursively(transform.GetChild(index).gameObject, layer);
    }

    public void SaveState(bool flushToDisk)
    {
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (manager == null || !manager.IsInterstellarGalaxy)
            return;

        UniversePosition position = ShipPhysicalUniversePosition;
        InterstellarCoordinate sector = manager.GetInterstellarScanCoordinate(position);
        float integrity = damageReceiver == null ? manager.SpacecraftHullIntegrity : damageReceiver.Integrity;
        manager.UpdateInterstellarFlightState(sector, position, integrity);
        if (flushToDisk)
            manager.FlushInterstellarFlightState();
    }

    void OnDisable()
    {
        if (initialized)
            SaveState(true);
        if (shipBody != null)
            shipBody.detectCollisions = originalDetectCollisions;
        if (presentationConfigured && localCamera != null)
        {
            localCamera.clearFlags = originalLocalClearFlags;
            localCamera.cullingMask = originalLocalCullingMask;
        }
        if (presentationConfigured && astronomicalCamera != null)
            astronomicalCamera.enabled = false;
    }

    void OnApplicationQuit()
    {
        if (initialized)
            SaveState(true);
    }
}
