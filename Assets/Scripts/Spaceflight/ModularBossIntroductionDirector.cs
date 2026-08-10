using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ModularBossIntroductionDirector : MonoBehaviour
{
    const float FadeInSeconds = 0.32f;
    const float EstablishingSeconds = 1.65f;
    const float ArsenalSeconds = 1.65f;
    const float ShieldSeconds = 1.50f;
    const float HandoffSeconds = 1.05f;
    const float EarliestSkipSeconds = 0.65f;
    const float CinematicNearClip = 0.08f;
    const int CameraHitCapacity = 32;

    readonly RaycastHit[] cameraHits = new RaycastHit[CameraHitCapacity];

    ModularBossCombatRuntime boss;
    PlanarSurfaceModularFlightController flightController;
    Rigidbody playerBody;
    Camera targetCamera;
    Coroutine routine;
    Action completed;
    string missionTitle = string.Empty;
    string dangerLabel = string.Empty;
    Vector3 savedCameraLocalPosition;
    Quaternion savedCameraLocalRotation = Quaternion.identity;
    float savedFieldOfView = 60f;
    float savedNearClip = 0.3f;
    float presentationElapsed;
    float fadeOpacity;
    float letterboxOpacity;
    float titleOpacity;
    int presentationBeat;
    bool skipRequested;

    public bool IsPlaying => routine != null;

    public bool Play(
        ModularBossCombatRuntime targetBoss,
        PlanarSurfaceModularFlightController targetFlightController,
        Rigidbody targetPlayerBody,
        Camera camera,
        string title,
        string difficulty,
        Action onCompleted)
    {
        if (targetBoss == null || targetFlightController == null ||
            targetPlayerBody == null || camera == null || routine != null)
        {
            return false;
        }

        boss = targetBoss;
        flightController = targetFlightController;
        playerBody = targetPlayerBody;
        targetCamera = camera;
        missionTitle = string.IsNullOrWhiteSpace(title)
            ? "首领拦截"
            : title;
        dangerLabel = difficulty ?? string.Empty;
        completed = onCompleted;
        presentationElapsed = 0f;
        fadeOpacity = 1f;
        letterboxOpacity = 1f;
        titleOpacity = 0f;
        presentationBeat = 0;
        skipRequested = false;

        Transform player = playerBody.transform;
        savedCameraLocalPosition = player.InverseTransformPoint(
            targetCamera.transform.position);
        savedCameraLocalRotation = Quaternion.Inverse(player.rotation) *
                                   targetCamera.transform.rotation;
        savedFieldOfView = targetCamera.fieldOfView;
        savedNearClip = targetCamera.nearClipPlane;

        boss.SetCombatActive(false);
        flightController.SetCinematicPresentation(true);
        targetCamera.nearClipPlane = Mathf.Min(
            targetCamera.nearClipPlane,
            CinematicNearClip);
        routine = StartCoroutine(PlayRoutine());
        return true;
    }

    public void Cancel(bool resumeGameplay)
    {
        if (routine != null)
            StopCoroutine(routine);
        routine = null;
        completed = null;
        RestoreCamera(true);
        if (flightController != null)
        {
            flightController.SetCinematicPresentation(false);
            flightController.SetGameplayReady(resumeGameplay);
        }
        ResetPresentation();
    }

    void Update()
    {
        if (routine == null)
            return;
        presentationElapsed += Time.unscaledDeltaTime;
        if (presentationElapsed < EarliestSkipSeconds)
            return;
        if (Input.GetKeyDown(KeyCode.Space) ||
            Input.GetKeyDown(KeyCode.Return) ||
            Input.GetKeyDown(KeyCode.KeypadEnter) ||
            Input.GetMouseButtonDown(0))
        {
            skipRequested = true;
        }
    }

    IEnumerator PlayRoutine()
    {
        Bounds bounds = ResolveBossBounds();
        Vector3 center = bounds.center;
        float radius = Mathf.Clamp(bounds.extents.magnitude, 12f, 82f);
        Vector3 up = Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(
            playerBody.worldCenterOfMass - center,
            up);
        if (forward.sqrMagnitude < 0.001f)
            forward = boss.transform.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(up, forward).normalized;

        Vector3 establishingStart = center -
            forward * (radius * 2.55f + 34f) -
            right * (radius * 0.68f + 10f) +
            up * (radius * 1.20f + 24f);
        Vector3 establishingEnd = center -
            forward * (radius * 2.20f + 28f) +
            right * (radius * 0.35f + 8f) +
            up * (radius * 0.92f + 20f);
        SetCameraPose(
            establishingStart,
            center + up * bounds.extents.y * 0.08f,
            52f,
            radius,
            true);

        float fadeElapsed = 0f;
        while (fadeElapsed < FadeInSeconds)
        {
            fadeElapsed += Time.unscaledDeltaTime;
            fadeOpacity = 1f - Mathf.Clamp01(
                fadeElapsed / FadeInSeconds);
            yield return null;
        }
        fadeOpacity = 0f;

        presentationBeat = 1;
        yield return AnimateShot(
            establishingStart,
            establishingEnd,
            center + up * bounds.extents.y * 0.08f,
            center + up * bounds.extents.y * 0.12f,
            52f,
            48f,
            EstablishingSeconds,
            radius);
        if (skipRequested)
        {
            yield return Handoff(0.18f);
            Finish();
            yield break;
        }

        presentationBeat = 2;
        Vector3 arsenalStart = center +
            forward * (radius * 1.72f + 18f) -
            right * (radius * 1.24f + 14f) +
            up * (radius * 0.34f + 7f);
        Vector3 arsenalEnd = center +
            forward * (radius * 1.54f + 16f) +
            right * (radius * 1.16f + 13f) +
            up * (radius * 0.50f + 9f);
        yield return AnimateShot(
            arsenalStart,
            arsenalEnd,
            center,
            center + up * bounds.extents.y * 0.08f,
            44f,
            40f,
            ArsenalSeconds,
            radius);
        if (skipRequested)
        {
            yield return Handoff(0.18f);
            Finish();
            yield break;
        }

        presentationBeat = 3;
        Vector3 shieldStart = center +
            forward * (radius * 1.42f + 13f) +
            right * (radius * 0.64f + 7f) +
            up * (radius * 0.58f + 10f);
        Vector3 shieldEnd = center +
            forward * (radius * 1.30f + 11f) -
            right * (radius * 0.24f + 4f) +
            up * (radius * 0.36f + 7f);
        yield return AnimateShot(
            shieldStart,
            shieldEnd,
            center + up * bounds.extents.y * 0.10f,
            center,
            38f,
            35f,
            ShieldSeconds,
            radius,
            true);

        yield return Handoff(
            skipRequested ? 0.18f : HandoffSeconds);
        Finish();
    }

    IEnumerator AnimateShot(
        Vector3 start,
        Vector3 end,
        Vector3 startFocus,
        Vector3 endFocus,
        float startFov,
        float endFov,
        float duration,
        float radius,
        bool showTitle = false)
    {
        float elapsed = 0f;
        while (elapsed < duration && !skipRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            float linear = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
            float blend = Mathf.SmoothStep(0f, 1f, linear);
            titleOpacity = showTitle
                ? Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.08f, 0.35f, linear))
                : 0f;
            SetCameraPose(
                Vector3.Lerp(start, end, blend),
                Vector3.Lerp(startFocus, endFocus, blend),
                Mathf.Lerp(startFov, endFov, blend),
                radius,
                false);
            yield return null;
        }
    }

    IEnumerator Handoff(float duration)
    {
        presentationBeat = 4;
        Vector3 startPosition = targetCamera.transform.position;
        Quaternion startRotation = targetCamera.transform.rotation;
        float startFov = targetCamera.fieldOfView;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float linear = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
            float blend = Mathf.SmoothStep(0f, 1f, linear);
            Vector3 targetPosition = playerBody.transform.TransformPoint(
                savedCameraLocalPosition);
            Quaternion targetRotation = playerBody.transform.rotation *
                                        savedCameraLocalRotation;
            Vector3 position = Vector3.Lerp(
                startPosition,
                targetPosition,
                blend);
            targetCamera.transform.SetPositionAndRotation(
                position,
                Quaternion.Slerp(startRotation, targetRotation, blend));
            targetCamera.fieldOfView = Mathf.Lerp(
                startFov,
                savedFieldOfView,
                blend);
            titleOpacity = 1f - blend;
            letterboxOpacity = 1f - blend;
            yield return null;
        }
        RestoreCamera(true);
    }

    void SetCameraPose(
        Vector3 desiredPosition,
        Vector3 focus,
        float fieldOfView,
        float bossRadius,
        bool snap)
    {
        if (targetCamera == null)
            return;
        Vector3 resolved = ResolveCameraPosition(
            focus,
            desiredPosition,
            bossRadius);
        Vector3 look = focus - resolved;
        if (look.sqrMagnitude < 0.001f)
            look = boss.transform.forward;
        Quaternion rotation = Quaternion.LookRotation(
            look.normalized,
            Vector3.up);
        if (snap)
        {
            targetCamera.transform.SetPositionAndRotation(
                resolved,
                rotation);
            targetCamera.fieldOfView = fieldOfView;
            return;
        }
        targetCamera.transform.SetPositionAndRotation(resolved, rotation);
        targetCamera.fieldOfView = fieldOfView;
    }

    Vector3 ResolveCameraPosition(
        Vector3 focus,
        Vector3 desired,
        float bossRadius)
    {
        Vector3 ray = desired - focus;
        float distance = ray.magnitude;
        if (distance <= 0.01f)
            return desired;
        Vector3 direction = ray / distance;
        int count = Physics.SphereCastNonAlloc(
            focus,
            0.8f,
            direction,
            cameraHits,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        float nearest = distance;
        for (int index = 0; index < count; index++)
        {
            Collider collider = cameraHits[index].collider;
            if (collider == null ||
                collider.transform.IsChildOf(boss.transform) ||
                collider.transform.IsChildOf(playerBody.transform) ||
                cameraHits[index].distance >= nearest)
            {
                continue;
            }
            nearest = cameraHits[index].distance;
        }
        if (nearest >= distance - 0.01f)
            return desired;
        float minimumDistance = Mathf.Max(8f, bossRadius * 1.08f);
        if (nearest <= minimumDistance + 1.5f)
        {
            return focus + direction * minimumDistance +
                   Vector3.up * Mathf.Max(8f, bossRadius * 0.65f);
        }
        return focus + direction * Mathf.Max(
            minimumDistance,
            nearest - 1.5f);
    }

    Bounds ResolveBossBounds()
    {
        if (boss != null && boss.StructureGraph != null)
        {
            Bounds resolved = boss.StructureGraph.ResolveVisualBounds();
            if (resolved.size.sqrMagnitude > 0.01f)
                return resolved;
        }
        return new Bounds(
            boss != null ? boss.transform.position : transform.position,
            Vector3.one * 24f);
    }

    void Finish()
    {
        routine = null;
        RestoreCamera(true);
        if (flightController != null)
        {
            flightController.SetCinematicPresentation(false);
            flightController.SetGameplayReady(true);
        }
        Action callback = completed;
        completed = null;
        ResetPresentation();
        callback?.Invoke();
    }

    void RestoreCamera(bool restoreTransform)
    {
        if (targetCamera == null)
            return;
        if (restoreTransform && playerBody != null)
        {
            targetCamera.transform.SetPositionAndRotation(
                playerBody.transform.TransformPoint(
                    savedCameraLocalPosition),
                playerBody.transform.rotation * savedCameraLocalRotation);
        }
        targetCamera.fieldOfView = savedFieldOfView;
        targetCamera.nearClipPlane = savedNearClip;
    }

    void ResetPresentation()
    {
        fadeOpacity = 0f;
        letterboxOpacity = 0f;
        titleOpacity = 0f;
        presentationBeat = 0;
        skipRequested = false;
    }

    void OnGUI()
    {
        if (routine == null)
            return;

        float width = Screen.width;
        float height = Screen.height;
        float barHeight = Mathf.Clamp(height * 0.095f, 54f, 108f);
        Color previousColor = GUI.color;
        int previousDepth = GUI.depth;
        GUI.depth = -1000;
        GUI.color = new Color(0f, 0f, 0f, letterboxOpacity * 0.96f);
        GUI.DrawTexture(
            new Rect(0f, 0f, width, barHeight),
            Texture2D.whiteTexture);
        GUI.DrawTexture(
            new Rect(0f, height - barHeight, width, barHeight),
            Texture2D.whiteTexture);

        if (presentationBeat == 1 || presentationBeat == 2)
        {
            string beat = presentationBeat == 1
                ? "威胁目标确认"
                : "武器与推进阵列扫描";
            GUIStyle beatStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(Screen.height / 38, 18, 30),
                fontStyle = FontStyle.Bold
            };
            beatStyle.normal.textColor = new Color(0.72f, 0.90f, 1f, 0.90f);
            GUI.color = Color.white;
            GUI.Label(
                new Rect(0f, height - barHeight + 4f, width, barHeight - 8f),
                beat,
                beatStyle);
        }

        if (titleOpacity > 0.001f)
        {
            GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(Screen.height / 22, 28, 52),
                fontStyle = FontStyle.Bold
            };
            titleStyle.normal.textColor = new Color(
                0.84f,
                0.95f,
                1f,
                titleOpacity);
            GUIStyle detailStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(Screen.height / 42, 16, 27)
            };
            detailStyle.normal.textColor = new Color(
                0.30f,
                0.86f,
                1f,
                titleOpacity * 0.95f);
            GUI.color = Color.white;
            GUI.Label(
                new Rect(0f, height * 0.63f, width, 72f),
                missionTitle,
                titleStyle);
            string details = string.IsNullOrWhiteSpace(dangerLabel)
                ? "模块化首领"
                : dangerLabel + "  ·  模块化首领";
            if (boss != null)
            {
                details += $"  ·  武器 {boss.LiveWeaponCount}" +
                           (boss.ShieldActive ? "  ·  护盾在线" : string.Empty);
            }
            GUI.Label(
                new Rect(0f, height * 0.63f + 58f, width, 46f),
                details,
                detailStyle);
        }

        if (presentationElapsed >= EarliestSkipSeconds)
        {
            GUIStyle skipStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontSize = Mathf.Clamp(Screen.height / 55, 14, 22)
            };
            skipStyle.normal.textColor = new Color(1f, 1f, 1f, 0.72f);
            GUI.color = Color.white;
            GUI.Label(
                new Rect(width - 360f, height - barHeight + 6f, 330f,
                    barHeight - 12f),
                "空格 / 回车 / 左键  跳过",
                skipStyle);
        }

        if (fadeOpacity > 0.001f)
        {
            GUI.color = new Color(0f, 0f, 0f, fadeOpacity);
            GUI.DrawTexture(
                new Rect(0f, 0f, width, height),
                Texture2D.whiteTexture);
        }
        GUI.color = previousColor;
        GUI.depth = previousDepth;
    }

    void OnDestroy()
    {
        if (routine == null)
            return;
        RestoreCamera(true);
        flightController?.SetCinematicPresentation(false);
    }
}
