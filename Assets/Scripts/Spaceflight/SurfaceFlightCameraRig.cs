using UnityEngine;

[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class SurfaceFlightCameraRig : MonoBehaviour
{
    [SerializeField, Min(1f)] float positionResponse = 6.5f;
    [SerializeField, Min(1f)] float rotationResponse = 8.5f;
    [SerializeField, Min(0f)] float maximumVelocityLag = 6f;
    [SerializeField, Range(0f, 1f)] float rollFollow = 0.34f;
    [SerializeField, Min(0f)] float maximumFovIncrease = 12f;
    [SerializeField, Min(1f)] float fullFovSpeed = 160f;
    [SerializeField, Min(0.1f)] float collisionRadius = 0.65f;
    [SerializeField, Range(10f, 35f)] float cityViewPitch = 18f;
    [SerializeField, Min(0.1f)] float cityTransitionDuration = 1f;

    readonly RaycastHit[] collisionHits = new RaycastHit[16];
    Camera targetCamera;
    Transform ship;
    Rigidbody shipBody;
    IPlanetSurfaceRuntime surfaceRuntime;
    Bounds shipBounds;
    float baseFieldOfView;
    bool active;

    Transform cityTarget;
    Bounds cityLocalBounds;
    Vector3 cityViewDirectionLocal;
    bool cityPresentationRequested;
    bool standalonePresentation;
    float cityPresentationWeight;
    Transform standaloneParent;
    Vector3 standaloneLocalPosition;
    Quaternion standaloneLocalRotation;
    Vector3 standaloneWorldPosition;
    Quaternion standaloneWorldRotation;
    float standaloneFieldOfView;
    VoxelPlanetPlayerController standaloneCameraOwner;

    public bool IsActive => active;
    public bool IsCityPresentationActive =>
        cityPresentationRequested || cityPresentationWeight > 0.001f;
    public bool IsCityPresentationSettled =>
        cityPresentationRequested
        && cityPresentationWeight >= 0.985f;
    public bool IsCityPresentationRestored =>
        !cityPresentationRequested
        && cityPresentationWeight <= 0.001f;
    public Vector3 CityViewDirectionLocal =>
        cityViewDirectionLocal;

    public void Activate(
        Camera camera,
        Transform targetShip,
        Rigidbody body,
        IPlanetSurfaceRuntime runtime,
        Bounds localBounds)
    {
        targetCamera = camera;
        EnsureFloatingOriginParticipation();
        ship = targetShip;
        shipBody = body;
        surfaceRuntime = runtime;
        shipBounds = localBounds;
        if (targetCamera != null)
            baseFieldOfView = targetCamera.fieldOfView;
        active = targetCamera != null && ship != null;
        enabled = active;
    }

    public bool BeginCityPresentation(
        Camera camera,
        Transform target,
        Bounds localBounds)
    {
        if (target == null)
            return false;
        if (targetCamera == null)
            targetCamera = camera != null ? camera : Camera.main;
        if (targetCamera == null)
            return false;
        if (surfaceRuntime == null)
            surfaceRuntime = PlanetSurfaceRuntimeRegistry.Current;

        cityTarget = target;
        cityLocalBounds = localBounds;
        ChooseCityViewDirection();
        cityPresentationRequested = true;
        if (!active && !standalonePresentation)
        {
            Transform cameraTransform = targetCamera.transform;
            standalonePresentation = true;
            standaloneParent = cameraTransform.parent;
            standaloneCameraOwner =
                cameraTransform.GetComponentInParent<
                    VoxelPlanetPlayerController>();
            standaloneCameraOwner
                ?.SetExternalCameraControl(true);
            standaloneLocalPosition = cameraTransform.localPosition;
            standaloneLocalRotation = cameraTransform.localRotation;
            standaloneWorldPosition = cameraTransform.position;
            standaloneWorldRotation = cameraTransform.rotation;
            standaloneFieldOfView = targetCamera.fieldOfView;
            baseFieldOfView = standaloneFieldOfView;
            EnsureFloatingOriginParticipation();
            cameraTransform.SetParent(null, true);
        }
        enabled = true;
        return true;
    }

    public void EndCityPresentation()
    {
        cityPresentationRequested = false;
    }

    public void ForceEndCityPresentation()
    {
        cityPresentationRequested = false;
        cityPresentationWeight = 0f;
        RestoreStandaloneCamera();
        cityTarget = null;
        if (!active)
            enabled = false;
    }

    public void Deactivate()
    {
        ForceEndCityPresentation();
        if (targetCamera != null && baseFieldOfView > 0f)
            targetCamera.fieldOfView = baseFieldOfView;
        active = false;
        ship = null;
        shipBody = null;
        surfaceRuntime = null;
        enabled = false;
    }

    void LateUpdate()
    {
        if (targetCamera == null)
            return;
        if (!active && !IsCityPresentationActive)
            return;

        float unscaledDelta = Mathf.Max(0f, Time.unscaledDeltaTime);
        float transitionStep = cityTransitionDuration <= 0.001f
            ? 1f
            : unscaledDelta / cityTransitionDuration;
        cityPresentationWeight = Mathf.MoveTowards(
            cityPresentationWeight,
            cityPresentationRequested ? 1f : 0f,
            transitionStep);

        ResolveBasePose(
            out Vector3 basePosition,
            out Quaternion baseRotation,
            out float targetFov);
        float blend = Mathf.SmoothStep(
            0f,
            1f,
            cityPresentationWeight);
        Vector3 desiredPosition = basePosition;
        Quaternion desiredRotation = baseRotation;
        if (cityTarget != null && blend > 0f)
        {
            ResolveCityPose(
                out Vector3 cityPosition,
                out Quaternion cityRotation,
                out float cityFov);
            desiredPosition = Vector3.Lerp(
                basePosition,
                cityPosition,
                blend);
            desiredRotation = Quaternion.Slerp(
                baseRotation,
                cityRotation,
                blend);
            targetFov = Mathf.Lerp(targetFov, cityFov, blend);
        }

        float positionBlend =
            1f - Mathf.Exp(-positionResponse * unscaledDelta);
        float rotationBlend =
            1f - Mathf.Exp(-rotationResponse * unscaledDelta);
        targetCamera.transform.position = Vector3.Lerp(
            targetCamera.transform.position,
            desiredPosition,
            positionBlend);
        targetCamera.transform.rotation = Quaternion.Slerp(
            targetCamera.transform.rotation,
            desiredRotation,
            rotationBlend);
        targetCamera.fieldOfView = Mathf.Lerp(
            targetCamera.fieldOfView,
            targetFov,
            1f - Mathf.Exp(-4.5f * unscaledDelta));

        if (!cityPresentationRequested
            && cityPresentationWeight <= 0.001f)
        {
            cityTarget = null;
            RestoreStandaloneCamera();
            if (!active)
                enabled = false;
        }
    }

    void ResolveBasePose(
        out Vector3 position,
        out Quaternion rotation,
        out float fov)
    {
        fov = baseFieldOfView > 0f
            ? baseFieldOfView
            : targetCamera.fieldOfView;
        if (!active || ship == null)
        {
            if (standalonePresentation
                && standaloneParent != null)
            {
                position = standaloneParent.TransformPoint(
                    standaloneLocalPosition);
                rotation = standaloneParent.rotation
                    * standaloneLocalRotation;
            }
            else
            {
                position = standaloneWorldPosition;
                rotation = standaloneWorldRotation;
            }
            return;
        }

        Vector3 up = GetSurfaceUp(ship.position);
        float distance = Mathf.Max(
            12f,
            shipBounds.extents.magnitude * 2.1f);
        float height = Mathf.Max(
            5f,
            shipBounds.extents.y + 3f);
        Vector3 focus =
            ship.position
            + up * Mathf.Max(
                1.5f,
                shipBounds.extents.y * 0.35f);
        Vector3 velocity = shipBody == null
            ? Vector3.zero
            : shipBody.velocity;
        Vector3 velocityLag = Vector3.ClampMagnitude(
            velocity * 0.045f,
            maximumVelocityLag);
        position = ship.position
            - ship.forward * distance
            + up * height
            - velocityLag;
        position = ResolveCameraCollision(focus, position);

        Vector3 cameraUp = Vector3.Slerp(
            up,
            ship.up,
            rollFollow);
        Vector3 lookDirection = focus - position;
        rotation = lookDirection.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(
                lookDirection,
                cameraUp)
            : targetCamera.transform.rotation;

        float speedRatio = Mathf.Clamp01(
            velocity.magnitude / Mathf.Max(1f, fullFovSpeed));
        fov += maximumFovIncrease
            * speedRatio
            * speedRatio;
    }

    void ResolveCityPose(
        out Vector3 position,
        out Quaternion rotation,
        out float fov)
    {
        Vector3 focus =
            cityTarget.TransformPoint(cityLocalBounds.center);
        Vector3 up = GetSurfaceUp(focus);
        Vector3 viewDirection =
            cityTarget.TransformDirection(cityViewDirectionLocal);
        viewDirection = Vector3.ProjectOnPlane(
            viewDirection,
            up).normalized;
        if (viewDirection.sqrMagnitude < 0.001f)
            viewDirection = Vector3.forward;

        float verticalFov = Mathf.Clamp(
            baseFieldOfView > 0f
                ? baseFieldOfView
                : targetCamera.fieldOfView,
            35f,
            70f);
        float aspect = Mathf.Max(0.2f, targetCamera.aspect);
        float horizontalFov = 2f * Mathf.Atan(
            Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad)
            * aspect);
        float longSize = Mathf.Max(
            cityLocalBounds.size.x,
            cityLocalBounds.size.z);
        float depthSize = Mathf.Min(
            cityLocalBounds.size.x,
            cityLocalBounds.size.z);
        float verticalSpan = Mathf.Max(
            8f,
            cityLocalBounds.size.y
                + depthSize
                    * Mathf.Sin(cityViewPitch * Mathf.Deg2Rad));
        float horizontalDistance =
            longSize * 0.6f
            / Mathf.Max(0.1f, Mathf.Tan(horizontalFov * 0.5f));
        float verticalDistance =
            verticalSpan * 0.6f
            / Mathf.Max(
                0.1f,
                Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad));
        float distance = Mathf.Clamp(
            Mathf.Max(35f, horizontalDistance, verticalDistance),
            35f,
            900f);
        float height = Mathf.Tan(
            cityViewPitch * Mathf.Deg2Rad) * distance;
        position = focus - viewDirection * distance + up * height;
        if (surfaceRuntime != null
            && surfaceRuntime.TryProjectToSurface(
                position,
                out PlanetSurfaceSample surface))
        {
            float clearance = Vector3.Dot(
                position - surface.point,
                up);
            if (clearance < 4f)
                position += up * (4f - clearance);
        }
        Vector3 look = focus - position;
        rotation = Quaternion.LookRotation(look.normalized, up);
        fov = verticalFov;
    }

    void ChooseCityViewDirection()
    {
        bool longAlongX =
            cityLocalBounds.size.x >= cityLocalBounds.size.z;
        Vector3 shortAxisLocal =
            longAlongX ? Vector3.forward : Vector3.right;
        Vector3 center = cityTarget.TransformPoint(
            cityLocalBounds.center);
        Vector3 axis = cityTarget.TransformDirection(
            shortAxisLocal).normalized;
        Vector3 fromCenter =
            targetCamera != null
                ? targetCamera.transform.position - center
                : axis;
        if (Vector3.Dot(axis, fromCenter) < 0f)
            axis = -axis;
        cityViewDirectionLocal =
            cityTarget.InverseTransformDirection(axis).normalized;
    }

    Vector3 GetSurfaceUp(Vector3 position)
    {
        Vector3 up = surfaceRuntime != null
            ? surfaceRuntime.GetUp(position)
            : Vector3.up;
        return up.sqrMagnitude > 0.0001f
            ? up.normalized
            : Vector3.up;
    }

    Vector3 ResolveCameraCollision(
        Vector3 focus,
        Vector3 desiredPosition)
    {
        Vector3 displacement = desiredPosition - focus;
        float distance = displacement.magnitude;
        if (distance <= 0.01f)
            return desiredPosition;

        Vector3 direction = displacement / distance;
        int count = Physics.SphereCastNonAlloc(
            focus,
            collisionRadius,
            direction,
            collisionHits,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        float nearest = distance;
        for (int index = 0; index < count; index++)
        {
            Collider hit = collisionHits[index].collider;
            if (hit == null
                || (ship != null
                    && hit.transform.IsChildOf(ship)))
            {
                continue;
            }
            nearest = Mathf.Min(
                nearest,
                collisionHits[index].distance);
        }
        return nearest < distance
            ? focus + direction * Mathf.Max(
                collisionRadius,
                nearest - collisionRadius)
            : desiredPosition;
    }

    void RestoreStandaloneCamera()
    {
        if (!standalonePresentation)
        {
            ReleaseStandaloneCameraOwner();
            return;
        }
        if (targetCamera == null)
        {
            standalonePresentation = false;
            ReleaseStandaloneCameraOwner();
            return;
        }
        Transform cameraTransform = targetCamera.transform;
        cameraTransform.SetParent(standaloneParent, false);
        cameraTransform.localPosition = standaloneLocalPosition;
        cameraTransform.localRotation = standaloneLocalRotation;
        targetCamera.fieldOfView = standaloneFieldOfView;
        standalonePresentation = false;
        ReleaseStandaloneCameraOwner();
    }

    void EnsureFloatingOriginParticipation()
    {
        if (targetCamera == null
            || targetCamera.GetComponent<
                PlanetFloatingOriginParticipant>() != null)
        {
            return;
        }
        targetCamera.gameObject.AddComponent<
            PlanetFloatingOriginParticipant>();
    }

    void ReleaseStandaloneCameraOwner()
    {
        if (standaloneCameraOwner != null)
            standaloneCameraOwner.SetExternalCameraControl(false);
        standaloneCameraOwner = null;
    }

    void OnDisable()
    {
        if (standalonePresentation)
            RestoreStandaloneCamera();
        if (targetCamera != null
            && baseFieldOfView > 0f
            && !active)
        {
            targetCamera.fieldOfView = baseFieldOfView;
        }
    }
}
