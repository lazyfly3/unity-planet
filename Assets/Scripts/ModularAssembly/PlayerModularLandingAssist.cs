using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    public static class PlayerLandingAssistPolicy
    {
        public static float ResolveTargetVerticalSpeed(
            VehicleCoreAssistMode mode,
            float heightAboveTouchdown,
            float upwardControlAcceleration,
            float gravityDownAcceleration)
        {
            float height = Mathf.Max(0f, heightAboveTouchdown);
            bool arcade = mode == VehicleCoreAssistMode.Training;
            float touchdownBand = arcade ? 0.55f : 0.75f;
            if (height <= touchdownBand)
                return arcade ? -0.35f : -0.25f;

            float usableBraking = Mathf.Max(
                0.25f,
                Mathf.Max(0f, upwardControlAcceleration) -
                Mathf.Max(0f, gravityDownAcceleration));
            float maximumDescentSpeed = arcade ? 8f : 5f;
            float brakingHeight = Mathf.Max(
                0f,
                height - touchdownBand);
            return -Mathf.Min(
                maximumDescentSpeed,
                Mathf.Sqrt(2f * usableBraking * brakingHeight));
        }

        public static Vector3 ResolvePlanarAcceleration(
            VehicleCoreAssistMode mode,
            Vector3 planarPositionError,
            Vector3 planarVelocity)
        {
            bool arcade = mode == VehicleCoreAssistMode.Training;
            float positionGain = arcade ? 1.8f : 0.65f;
            float velocityGain = arcade ? 2.8f : 1.55f;
            float maximumAcceleration = arcade ? 8f : 3.5f;
            return Vector3.ClampMagnitude(
                planarPositionError * positionGain -
                planarVelocity * velocityGain,
                maximumAcceleration);
        }

        public static Vector3 ResolvePlanarHeading(
            Vector3 preferredForward,
            Vector3 surfaceNormal,
            Vector3 fallbackRight)
        {
            Vector3 up = surfaceNormal.sqrMagnitude > 0.0001f
                ? surfaceNormal.normalized
                : Vector3.up;
            Vector3 heading = Vector3.ProjectOnPlane(
                preferredForward,
                up);
            if (heading.sqrMagnitude < 0.0001f)
            {
                heading = Vector3.Cross(
                    fallbackRight.sqrMagnitude > 0.0001f
                        ? fallbackRight.normalized
                        : Vector3.right,
                    up);
            }
            if (heading.sqrMagnitude < 0.0001f)
                heading = Vector3.ProjectOnPlane(Vector3.forward, up);
            return heading.sqrMagnitude > 0.0001f
                ? heading.normalized
                : Vector3.forward;
        }
    }

    /// <summary>
    /// Player-only C-key landing owner. It feeds a landing request into the
    /// existing RC3 actuator allocator; it never writes Rigidbody velocity or
    /// bypasses installed/core authority. Boss runtimes do not add this
    /// component.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class PlayerModularLandingAssist : MonoBehaviour
    {
        const float MinimumHullClearance = 0.35f;
        const float CollisionClearance = 0.12f;
        const float ClearanceRefreshSeconds = 0.2f;
        const float GroundContactMemorySeconds = 0.16f;
        const float MaximumGroundProbeDistance = 20000f;

        Rigidbody body;
        RobocraftMotionCoordinator motion;
        IPlanetEnvironmentProvider environmentProvider;
        Vector3 landingAnchorWorld;
        Vector3 landingHeadingWorld;
        Vector3 landingSurfaceNormal = Vector3.up;
        float landingClearance = MinimumHullClearance;
        float nextClearanceRefresh;
        float groundContactUntil;
        bool ownsInjectedControl;
        readonly RaycastHit[] groundHits = new RaycastHit[64];

        public bool IsLanding { get; private set; }
        public string LastStatus { get; private set; } = string.Empty;

        public void Configure(
            Rigidbody targetBody,
            RobocraftMotionCoordinator motionCoordinator,
            IPlanetEnvironmentProvider environment = null)
        {
            if (IsLanding)
                StopLanding("Landing configuration changed.");
            body = targetBody;
            motion = motionCoordinator;
            environmentProvider = environment;
            landingSurfaceNormal = Vector3.up;
            landingClearance = MinimumHullClearance;
            nextClearanceRefresh = 0f;
            groundContactUntil = 0f;
            LastStatus = string.Empty;
        }

        void Update()
        {
            if (body == null || motion == null)
                return;
            if (ArcadeFlightRuntimeTuningOverlay.IsInputCaptured ||
                ModularSpacecraftPauseMenu.IsOpen)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.C))
            {
                if (IsLanding)
                    StopLanding("Landing cancelled.");
                else
                    TryStartLanding();
            }

            if (IsLanding &&
                (!motion.IsActive ||
                 !motion.ControlsEnabled ||
                 body.isKinematic))
            {
                StopLanding("Landing stopped because flight control ended.");
            }
        }

        void FixedUpdate()
        {
            if (!IsLanding || body == null || motion == null)
                return;
            if (!TrySampleEnvironment(out PlanetEnvironmentSample sample))
            {
                StopLanding("No landable surface is available.");
                return;
            }

            Vector3 targetNormal = ResolveSurfaceNormal(sample);
            float normalBlend = 1f - Mathf.Exp(
                -8f * Time.fixedDeltaTime);
            landingSurfaceNormal = Vector3.Slerp(
                landingSurfaceNormal,
                targetNormal,
                normalBlend).normalized;
            landingHeadingWorld =
                PlayerLandingAssistPolicy.ResolvePlanarHeading(
                    landingHeadingWorld,
                    landingSurfaceNormal,
                    transform.right);

            if (Time.fixedTime >= nextClearanceRefresh)
            {
                landingClearance = CalculateHullClearance(
                    landingSurfaceNormal);
                nextClearanceRefresh =
                    Time.fixedTime + ClearanceRefreshSeconds;
            }

            motion.SetInjectedControl(new RobocraftControlFrame
            {
                freeLook = true,
                hasAimOverride = true,
                aimForwardWorld = landingHeadingWorld,
                landingRequested = true,
                landingAnchorWorld = landingAnchorWorld,
                landingSurfacePointWorld = sample.surfacePoint,
                landingSurfaceNormalWorld = landingSurfaceNormal,
                landingClearance = landingClearance
            });
            ownsInjectedControl = true;

            float height = HeightAboveTouchdown(
                sample,
                body.worldCenterOfMass,
                landingSurfaceNormal,
                landingClearance);
            float verticalSpeed = Vector3.Dot(
                body.velocity,
                landingSurfaceNormal);
            float planarSpeed = Vector3.ProjectOnPlane(
                body.velocity,
                landingSurfaceNormal).magnitude;
            float alignment = Vector3.Dot(
                transform.up,
                landingSurfaceNormal);
            bool recentGroundContact =
                Time.fixedTime <= groundContactUntil;
            bool settled = height <= 0.28f &&
                           Mathf.Abs(verticalSpeed) <= 0.8f &&
                           planarSpeed <= 0.9f &&
                           alignment >= 0.94f;
            if (settled && (recentGroundContact || height <= 0.06f))
                StopLanding("Landing complete.");
        }

        bool TryStartLanding()
        {
            if (!motion.IsActive ||
                !motion.ControlsEnabled ||
                body.isKinematic)
            {
                LastStatus = "Landing is unavailable outside active flight.";
                return false;
            }
            if (motion.InjectedControlActive)
            {
                LastStatus = "Landing is unavailable during automatic control.";
                return false;
            }
            if (!TrySampleEnvironment(out PlanetEnvironmentSample sample))
            {
                LastStatus = "No landable surface is available.";
                return false;
            }

            landingSurfaceNormal = ResolveSurfaceNormal(sample);
            landingHeadingWorld =
                PlayerLandingAssistPolicy.ResolvePlanarHeading(
                    transform.forward,
                    landingSurfaceNormal,
                    transform.right);
            landingAnchorWorld = body.worldCenterOfMass;
            landingClearance = CalculateHullClearance(
                landingSurfaceNormal);
            nextClearanceRefresh =
                Time.fixedTime + ClearanceRefreshSeconds;
            groundContactUntil = 0f;
            ownsInjectedControl = false;
            IsLanding = true;
            LastStatus = motion.CoreAssistMode ==
                         VehicleCoreAssistMode.Training
                ? "Arcade landing engaged. Press C to cancel."
                : "Standard landing engaged. Press C to cancel.";
            return true;
        }

        void StopLanding(string status)
        {
            if (ownsInjectedControl && motion != null)
                motion.ClearInjectedControl();
            ownsInjectedControl = false;
            IsLanding = false;
            LastStatus = status ?? string.Empty;
        }

        bool TrySampleEnvironment(out PlanetEnvironmentSample sample)
        {
            sample = default;
            if (body == null)
                return false;
            IPlanetEnvironmentProvider provider =
                environmentProvider ?? PlanetEnvironmentRuntime.Active;
            bool hasEnvironmentSurface = false;
            if (provider != null)
            {
                sample = provider.Sample(
                    body.worldCenterOfMass,
                    Time.fixedTimeAsDouble);
                hasEnvironmentSurface = sample.hasSurface &&
                                        IsFinite(sample.surfacePoint) &&
                                        IsFinite(sample.surfaceNormal);
            }

            Vector3 gravity = sample.gravityAcceleration.sqrMagnitude >
                              0.0001f
                ? sample.gravityAcceleration
                : Vector3.down * 9.81f;
            Vector3 down = gravity.normalized;
            float probeDistance = hasEnvironmentSurface
                ? Mathf.Clamp(
                    Mathf.Max(50f, sample.surfaceDistance + 100f),
                    50f,
                    MaximumGroundProbeDistance)
                : MaximumGroundProbeDistance;
            if (TryProbePhysicalSurface(
                    body.worldCenterOfMass,
                    down,
                    probeDistance,
                    out RaycastHit groundHit))
            {
                sample.gravityAcceleration = gravity;
                sample.surfacePoint = groundHit.point;
                sample.surfaceNormal = groundHit.normal;
                sample.surfaceDistance = groundHit.distance;
                sample.altitude = Mathf.Max(0f, groundHit.distance);
                sample.hasSurface = true;
                return true;
            }
            return hasEnvironmentSurface;
        }

        bool TryProbePhysicalSurface(
            Vector3 origin,
            Vector3 down,
            float maximumDistance,
            out RaycastHit nearest)
        {
            nearest = default;
            if (down.sqrMagnitude < 0.0001f)
                return false;
            int hitCount = Physics.RaycastNonAlloc(
                origin,
                down.normalized,
                groundHits,
                Mathf.Max(0.1f, maximumDistance),
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            float nearestDistance = float.PositiveInfinity;
            bool found = false;
            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = groundHits[index];
                if (hit.collider == null ||
                    hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }
                Rigidbody hitBody = hit.collider.attachedRigidbody;
                if (hitBody != null && hitBody != body)
                    continue;
                if (hit.distance >= nearestDistance)
                    continue;
                nearest = hit;
                nearestDistance = hit.distance;
                found = true;
            }
            return found;
        }

        Vector3 ResolveSurfaceNormal(PlanetEnvironmentSample sample)
        {
            Vector3 gravityUp = sample.gravityAcceleration.sqrMagnitude >
                                0.0001f
                ? -sample.gravityAcceleration.normalized
                : Vector3.up;
            Vector3 normal = sample.surfaceNormal.sqrMagnitude > 0.0001f
                ? sample.surfaceNormal.normalized
                : gravityUp;
            if (Vector3.Dot(normal, gravityUp) < 0.2f)
                normal = gravityUp;
            return normal;
        }

        float CalculateHullClearance(Vector3 surfaceNormal)
        {
            if (body == null)
                return MinimumHullClearance;
            Vector3 up = surfaceNormal.sqrMagnitude > 0.0001f
                ? surfaceNormal.normalized
                : Vector3.up;
            Vector3 center = body.worldCenterOfMass;
            float lowestProjection = 0f;
            bool found = false;
            foreach (Collider collider in
                     body.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null ||
                    !collider.enabled ||
                    collider.isTrigger ||
                    !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }
                Bounds bounds = collider.bounds;
                Vector3 extents = bounds.extents;
                float projectedRadius =
                    Mathf.Abs(up.x) * extents.x +
                    Mathf.Abs(up.y) * extents.y +
                    Mathf.Abs(up.z) * extents.z;
                float lower = Vector3.Dot(
                    bounds.center - center,
                    up) - projectedRadius;
                lowestProjection = found
                    ? Mathf.Min(lowestProjection, lower)
                    : lower;
                found = true;
            }
            return found
                ? Mathf.Max(
                    MinimumHullClearance,
                    -lowestProjection + CollisionClearance)
                : MinimumHullClearance;
        }

        static float HeightAboveTouchdown(
            PlanetEnvironmentSample sample,
            Vector3 centerOfMass,
            Vector3 surfaceNormal,
            float clearance)
        {
            Vector3 up = surfaceNormal.sqrMagnitude > 0.0001f
                ? surfaceNormal.normalized
                : Vector3.up;
            return Mathf.Max(
                0f,
                Vector3.Dot(
                    centerOfMass - sample.surfacePoint,
                    up) - Mathf.Max(0f, clearance));
        }

        void OnCollisionEnter(Collision collision)
        {
            RememberGroundContact(collision);
        }

        void OnCollisionStay(Collision collision)
        {
            RememberGroundContact(collision);
        }

        void RememberGroundContact(Collision collision)
        {
            if (!IsLanding || collision == null)
                return;
            for (int index = 0; index < collision.contactCount; index++)
            {
                if (Vector3.Dot(
                        collision.GetContact(index).normal,
                        landingSurfaceNormal) <= 0.45f)
                {
                    continue;
                }
                groundContactUntil =
                    Time.fixedTime + GroundContactMemorySeconds;
                return;
            }
        }

        void OnDisable()
        {
            if (IsLanding || ownsInjectedControl)
                StopLanding("Landing controller disabled.");
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
