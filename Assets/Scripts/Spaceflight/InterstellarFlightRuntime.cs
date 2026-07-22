using System;
using SpacecraftEditor;
using UnityEngine;

[DefaultExecutionOrder(-700)]
[DisallowMultipleComponent]
public sealed class InterstellarFlightRuntime : MonoBehaviour
{
    [SerializeField] Rigidbody shipBody;
    [SerializeField] SpacecraftDamageReceiver damageReceiver;
    [SerializeField, Min(250f)] float floatingOriginThreshold = 2000f;
    [SerializeField, Min(0.25f)] float saveInterval = 2f;

    DoubleVector3 universeOrigin;
    float nextSaveTime;
    bool initialized;

    public event Action<Vector3> OriginShifted;
    public Rigidbody ShipBody => shipBody;
    public DoubleVector3 UniverseOrigin => universeOrigin;
    public DoubleVector3 ShipUniversePosition => universeOrigin + new DoubleVector3(
        shipBody == null ? 0d : shipBody.position.x,
        shipBody == null ? 0d : shipBody.position.y,
        shipBody == null ? 0d : shipBody.position.z);

    public Vector3 ToLocalPosition(DoubleVector3 universePosition)
    {
        DoubleVector3 offset = universePosition - universeOrigin;
        return offset.ToVector3();
    }

    public DoubleVector3 ToUniversePosition(Vector3 localPosition)
    {
        return universeOrigin + new DoubleVector3(localPosition.x, localPosition.y, localPosition.z);
    }

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

        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        universeOrigin = manager != null && manager.IsInterstellarGalaxy
            ? manager.SavedSpacePosition
            : DoubleVector3.Zero;
        if (shipBody != null)
            shipBody.position = Vector3.zero;
        initialized = true;
    }

    void FixedUpdate()
    {
        if (!initialized || shipBody == null)
            return;

        if (shipBody.position.sqrMagnitude >= floatingOriginThreshold * floatingOriginThreshold)
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
        universeOrigin += new DoubleVector3(localShift.x, localShift.y, localShift.z);
        RigidbodyInterpolation interpolation = shipBody.interpolation;
        shipBody.interpolation = RigidbodyInterpolation.None;
        shipBody.position -= localShift;
        Physics.SyncTransforms();
        OriginShifted?.Invoke(localShift);
        shipBody.interpolation = interpolation;
    }

    public void SaveState(bool flushToDisk)
    {
        GalaxyTravelManager manager = GalaxyTravelManager.Instance;
        if (manager == null || !manager.IsInterstellarGalaxy)
            return;

        DoubleVector3 position = ShipUniversePosition;
        InterstellarCoordinate sector = new InterstellarCoordinate(
            RoundToLong(position.x / ProceduralInterstellarGenerator.SectorSpacing),
            RoundToLong(position.y / ProceduralInterstellarGenerator.SectorSpacing),
            RoundToLong(position.z / ProceduralInterstellarGenerator.SectorSpacing));
        float integrity = damageReceiver == null ? manager.SpacecraftHullIntegrity : damageReceiver.Integrity;
        manager.UpdateInterstellarFlightState(sector, position, integrity);
        if (flushToDisk)
            manager.FlushInterstellarFlightState();
    }

    static long RoundToLong(double value)
    {
        if (value >= long.MaxValue)
            return long.MaxValue;
        if (value <= long.MinValue)
            return long.MinValue;
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    void OnDisable()
    {
        if (initialized)
            SaveState(true);
    }

    void OnApplicationQuit()
    {
        if (initialized)
            SaveState(true);
    }
}
