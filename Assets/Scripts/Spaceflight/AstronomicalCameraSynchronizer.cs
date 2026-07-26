using UnityEngine;

[DefaultExecutionOrder(1200)]
[DisallowMultipleComponent]
public sealed class AstronomicalCameraSynchronizer : MonoBehaviour
{
    Camera astronomicalCamera;
    Camera localCamera;
    Rigidbody shipBody;
    InterstellarFlightRuntime runtime;
    int kilometerLayerMask;

    public void Configure(
        Camera astronomical,
        Camera local,
        Rigidbody ship,
        InterstellarFlightRuntime flightRuntime)
    {
        astronomicalCamera = astronomical;
        localCamera = local;
        shipBody = ship;
        runtime = flightRuntime;
        int kilometerLayer = LayerMask.NameToLayer("SpaceKilometerView");
        kilometerLayerMask = kilometerLayer < 0 ? 0 : 1 << kilometerLayer;
        Synchronize();
    }

    void LateUpdate()
    {
        Synchronize();
    }

    void Synchronize()
    {
        if (astronomicalCamera == null || localCamera == null)
            return;

        // Camera components can be reconfigured by cockpit/presentation systems
        // after the flight runtime's Awake. Reassert the two-camera composition
        // here so the local camera cannot clear the already rendered planet.
        if (kilometerLayerMask != 0)
        {
            localCamera.cullingMask &= ~kilometerLayerMask;
            astronomicalCamera.cullingMask = kilometerLayerMask;
        }
        localCamera.clearFlags = CameraClearFlags.Depth;
        astronomicalCamera.clearFlags = CameraClearFlags.SolidColor;
        astronomicalCamera.backgroundColor = Color.black;
        astronomicalCamera.depth = localCamera.depth - 10f;

        Vector3 shipPositionMeters = shipBody == null
            ? Vector3.zero
            : shipBody.position;
        Vector3 referenceFramePosition = runtime != null
            ? runtime.ShipPlanetRelativePositionKilometers
            : Vector3.zero;
        astronomicalCamera.transform.SetPositionAndRotation(
            referenceFramePosition
                + SpaceKilometerScale.ToKilometerUnits(
                    localCamera.transform.position - shipPositionMeters),
            localCamera.transform.rotation);
        astronomicalCamera.fieldOfView = localCamera.fieldOfView;
        astronomicalCamera.orthographic = localCamera.orthographic;
        astronomicalCamera.orthographicSize = SpaceKilometerScale.ToKilometerUnits(
            localCamera.orthographicSize);
        astronomicalCamera.aspect = localCamera.aspect;
        astronomicalCamera.rect = localCamera.rect;
    }
}
