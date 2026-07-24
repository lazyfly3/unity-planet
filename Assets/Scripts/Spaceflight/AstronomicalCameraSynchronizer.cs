using UnityEngine;

[DefaultExecutionOrder(1200)]
[DisallowMultipleComponent]
public sealed class AstronomicalCameraSynchronizer : MonoBehaviour
{
    Camera astronomicalCamera;
    Camera localCamera;
    Rigidbody shipBody;

    public void Configure(
        Camera astronomical,
        Camera local,
        Rigidbody ship)
    {
        astronomicalCamera = astronomical;
        localCamera = local;
        shipBody = ship;
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

        Vector3 shipPositionMeters = shipBody == null
            ? Vector3.zero
            : shipBody.position;
        astronomicalCamera.transform.SetPositionAndRotation(
            SpaceKilometerScale.ToKilometerUnits(
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
