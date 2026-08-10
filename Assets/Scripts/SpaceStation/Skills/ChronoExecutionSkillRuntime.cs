using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.SpaceStation.Skills
{
    [DisallowMultipleComponent]
    public sealed class ChronoExecutionSkillRuntime : MonoBehaviour
    {
        sealed class TargetLock
        {
            public int Id;
            public Transform Root;
            public ISpaceDamageable Damageable;
            public Vector3 LocalAimPoint;
            public float Progress;
            public float LastSeenAt;
            public bool Visible;
            public RectTransform MarkerRect;
            public ChronoExecutionTargetGraphic MarkerGraphic;

            public Vector3 AimPoint => Root == null
                ? Vector3.zero
                : Root.TransformPoint(LocalAimPoint);
        }

        readonly struct Candidate
        {
            public readonly int Id;
            public readonly Transform Root;
            public readonly ISpaceDamageable Damageable;
            public readonly Vector3 AimPoint;
            public readonly float Score;

            public Candidate(
                int id,
                Transform root,
                ISpaceDamageable damageable,
                Vector3 aimPoint,
                float score)
            {
                Id = id;
                Root = root;
                Damageable = damageable;
                AimPoint = aimPoint;
                Score = score;
            }
        }

        const int MaximumTargets = 8;
        const float MaximumRange = 1400f;
        const float FieldOfViewDegrees = 105f;
        const float FreezeDuration = 0.16f;
        const float TotalDuration = 3.6f;
        const float SlowTimeScale = 0.18f;
        const float TargetGraceSeconds = 0.75f;

        readonly Collider[] overlaps = new Collider[192];
        readonly RaycastHit[] lineOfSightHits = new RaycastHit[64];
        readonly Dictionary<int, TargetLock> locks =
            new Dictionary<int, TargetLock>();
        readonly List<Candidate> candidates = new List<Candidate>(64);
        readonly HashSet<int> candidateIds = new HashSet<int>();
        readonly List<int> removalBuffer = new List<int>();

        VehicleStructureGraph graph;
        Camera targetCamera;
        Canvas canvas;
        RectTransform canvasRect;
        Image temporalOverlay;
        Text statusText;
        float startedAt;
        float nextScanAt;
        float previousTimeScale = 1f;
        float previousFixedDeltaTime = 0.02f;
        float lastAppliedTimeScale = 1f;
        bool active;

        public bool Active => active;
        public int TargetCount => locks.Count;
        public int FullyLockedCount
        {
            get
            {
                int count = 0;
                foreach (TargetLock target in locks.Values)
                    if (target.Visible && target.Progress >= 0.999f)
                        count++;
                return count;
            }
        }
        public float NormalizedRemaining => !active
            ? 0f
            : Mathf.Clamp01(
                1f - (Time.unscaledTime - startedAt) / TotalDuration);

        public event Action<int, int> Finished;

        public void Initialize(VehicleStructureGraph target)
        {
            graph = target;
        }

        public bool Begin()
        {
            if (active || graph == null || !graph.Active)
                return false;
            targetCamera = ResolveCamera();
            if (targetCamera == null)
                return false;

            previousTimeScale = Time.timeScale;
            previousFixedDeltaTime = Time.fixedDeltaTime;
            lastAppliedTimeScale = previousTimeScale;
            startedAt = Time.unscaledTime;
            nextScanAt = 0f;
            active = true;
            EnsureHud();
            canvas.gameObject.SetActive(true);
            ScanTargets();
            ApplyTemporalScale(0.02f);
            return true;
        }

        public void Execute()
        {
            if (!active)
                return;
            if (ModularSpacecraftPauseMenu.IsOpen)
                return;
            int fired = 0;
            int lethal = 0;
            Vector3 sourcePoint = graph.ResolveVisualBounds().center;
            foreach (TargetLock target in new List<TargetLock>(locks.Values))
            {
                if (target.Root == null || target.Damageable == null ||
                    target.Damageable.IsDestroyed || !target.Visible ||
                    target.Progress <= 0.05f)
                    continue;
                Vector3 aimPoint = target.AimPoint;
                if (!HasLineOfSight(target.Root, aimPoint))
                    continue;
                float progress = Mathf.Clamp01(target.Progress);
                float integrity = Mathf.Max(1f, target.Damageable.Integrity);
                float damage = progress >= 0.999f
                    ? integrity + Mathf.Max(
                        1f,
                        target.Damageable.MaximumIntegrity * 0.05f)
                    : integrity * progress;
                Vector3 direction = (aimPoint - sourcePoint).normalized;
                target.Damageable.ApplyDamage(new SpaceDamageInfo(
                    damage,
                    aimPoint,
                    direction * Mathf.Min(18f, damage * 0.04f),
                    SpaceDamageType.Projectile,
                    graph.gameObject));
                ChronoExecutionTracer.Spawn(sourcePoint, aimPoint);
                fired++;
                if (progress >= 0.999f)
                    lethal++;
            }
            Finish(fired, lethal);
        }

        public void Cancel(bool notify = true)
        {
            if (!active)
                return;
            if (notify)
                Finish(0, 0);
            else
            {
                RestoreTimeScale();
                active = false;
                ClearLocks();
                if (canvas != null)
                    canvas.gameObject.SetActive(false);
            }
        }

        void Update()
        {
            if (!active)
                return;
            if (graph == null || !graph.Active || graph.IsVehicleDestroyed)
            {
                Finish(0, 0);
                return;
            }

            float elapsed = Time.unscaledTime - startedAt;
            ApplyTemporalScale(
                elapsed < FreezeDuration ? 0.02f : SlowTimeScale);
            if (Time.unscaledTime >= nextScanAt)
            {
                nextScanAt = Time.unscaledTime + 0.06f;
                ScanTargets();
            }
            TickLockProgress(Time.unscaledDeltaTime);
            UpdateHud(elapsed);

            if (elapsed >= 0.22f && Input.GetMouseButtonDown(0))
            {
                Execute();
                return;
            }
            if (elapsed >= TotalDuration)
                Execute();
        }

        void OnDisable()
        {
            if (active)
                Cancel(false);
        }

        void OnDestroy()
        {
            if (active)
                RestoreTimeScale();
            if (canvas != null)
                Destroy(canvas.gameObject);
        }

        void ScanTargets()
        {
            targetCamera = ResolveCamera();
            if (targetCamera == null)
                return;
            foreach (TargetLock target in locks.Values)
                target.Visible = false;

            candidates.Clear();
            candidateIds.Clear();
            Vector3 origin = targetCamera.transform.position;
            Vector3 forward = targetCamera.transform.forward;
            float minimumDot = Mathf.Cos(
                FieldOfViewDegrees * 0.5f * Mathf.Deg2Rad);
            int count = Physics.OverlapSphereNonAlloc(
                origin,
                MaximumRange,
                overlaps,
                ~0,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider collider = overlaps[index];
                if (collider == null ||
                    collider.transform.IsChildOf(graph.transform))
                    continue;
                ISpaceDamageable damageable =
                    WeaponDamageUtility.FindDamageable(collider.transform);
                Component damageComponent = damageable as Component;
                if (damageComponent == null || damageable.IsDestroyed)
                    continue;
                VehicleCombatTeamMarker marker =
                    damageComponent.GetComponentInParent<
                        VehicleCombatTeamMarker>();
                Transform root = marker != null
                    ? marker.transform
                    : damageComponent.transform;
                if (VehicleCombatTeamUtility.Resolve(root) !=
                    VehicleCombatTeam.Enemy)
                    continue;
                int id = root.GetInstanceID();
                if (!candidateIds.Add(id))
                    continue;
                Vector3 aimPoint = ResolveAimPoint(root, collider);
                Vector3 offset = aimPoint - origin;
                float distance = offset.magnitude;
                if (distance <= 0.1f || distance > MaximumRange)
                    continue;
                Vector3 direction = offset / distance;
                float facing = Vector3.Dot(forward, direction);
                if (facing < minimumDot)
                    continue;
                Vector3 viewport =
                    targetCamera.WorldToViewportPoint(aimPoint);
                if (viewport.z <= 0f || viewport.x < -0.05f ||
                    viewport.x > 1.05f || viewport.y < -0.05f ||
                    viewport.y > 1.05f)
                    continue;
                float screenOffset = new Vector2(
                    (viewport.x - 0.5f) * targetCamera.aspect,
                    viewport.y - 0.5f).sqrMagnitude;
                candidates.Add(new Candidate(
                    id,
                    root,
                    damageable,
                    aimPoint,
                    screenOffset * 4f + distance / MaximumRange));
            }

            candidates.Sort((left, right) =>
                left.Score.CompareTo(right.Score));
            int accepted = Mathf.Min(MaximumTargets, candidates.Count);
            for (int index = 0; index < accepted; index++)
            {
                Candidate candidate = candidates[index];
                if (!HasLineOfSight(candidate.Root, candidate.AimPoint))
                    continue;
                if (!locks.TryGetValue(candidate.Id, out TargetLock target))
                {
                    target = new TargetLock { Id = candidate.Id };
                    locks.Add(candidate.Id, target);
                    CreateMarker(target);
                }
                target.Root = candidate.Root;
                target.Damageable = candidate.Damageable;
                target.LocalAimPoint = candidate.Root.InverseTransformPoint(
                    candidate.AimPoint);
                target.Visible = true;
                target.LastSeenAt = Time.unscaledTime;
            }

            removalBuffer.Clear();
            foreach (KeyValuePair<int, TargetLock> pair in locks)
            {
                TargetLock target = pair.Value;
                if (target.Root == null || target.Damageable == null ||
                    target.Damageable.IsDestroyed ||
                    Time.unscaledTime - target.LastSeenAt >
                    TargetGraceSeconds)
                    removalBuffer.Add(pair.Key);
            }
            foreach (int id in removalBuffer)
                RemoveLock(id);
        }

        void TickLockProgress(float unscaledDeltaTime)
        {
            foreach (TargetLock target in locks.Values)
            {
                if (!target.Visible || target.Damageable == null ||
                    target.Damageable.IsDestroyed)
                    continue;
                float healthRatio = target.Damageable.MaximumIntegrity <= 0f
                    ? 1f
                    : Mathf.Clamp01(
                        target.Damageable.Integrity /
                        target.Damageable.MaximumIntegrity);
                float requiredSeconds = Mathf.Lerp(
                    0.55f,
                    1.9f,
                    healthRatio);
                target.Progress = Mathf.Clamp01(
                    target.Progress + unscaledDeltaTime / requiredSeconds);
            }
        }

        bool HasLineOfSight(Transform targetRoot, Vector3 aimPoint)
        {
            if (targetCamera == null || targetRoot == null)
                return false;
            Vector3 origin = targetCamera.transform.position;
            Vector3 offset = aimPoint - origin;
            float distance = offset.magnitude;
            if (distance <= 0.05f)
                return true;
            int count = Physics.RaycastNonAlloc(
                origin,
                offset / distance,
                lineOfSightHits,
                distance + 1.5f,
                ~0,
                QueryTriggerInteraction.Ignore);
            float nearestDistance = float.PositiveInfinity;
            Transform nearest = null;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = lineOfSightHits[index];
                if (hit.collider == null ||
                    hit.collider.transform.IsChildOf(graph.transform) ||
                    hit.distance >= nearestDistance)
                    continue;
                nearestDistance = hit.distance;
                nearest = hit.collider.transform;
            }
            return nearest == null || nearest == targetRoot ||
                   nearest.IsChildOf(targetRoot) ||
                   targetRoot.IsChildOf(nearest);
        }

        static Vector3 ResolveAimPoint(
            Transform root,
            Collider fallbackCollider)
        {
            Rigidbody body = root.GetComponent<Rigidbody>() ??
                             root.GetComponentInChildren<Rigidbody>();
            if (body != null)
                return body.worldCenterOfMass;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int index = 1; index < renderers.Length; index++)
                    if (renderers[index] != null &&
                        renderers[index].enabled)
                        bounds.Encapsulate(renderers[index].bounds);
                return bounds.center;
            }
            return fallbackCollider != null
                ? fallbackCollider.bounds.center
                : root.position;
        }

        Camera ResolveCamera()
        {
            WeaponSystemCoordinator weapons = graph == null
                ? null
                : graph.GetComponent<WeaponSystemCoordinator>();
            if (weapons != null && weapons.SceneCamera != null)
                return weapons.SceneCamera;
            return Camera.main;
        }

        void ApplyTemporalScale(float scale)
        {
            scale = Mathf.Clamp(scale, 0.01f, 1f);
            Time.timeScale = scale;
            float baselineScale = Mathf.Max(0.01f, previousTimeScale);
            Time.fixedDeltaTime = Mathf.Max(
                0.001f,
                previousFixedDeltaTime * scale / baselineScale);
            lastAppliedTimeScale = scale;
        }

        void RestoreTimeScale()
        {
            if (Mathf.Abs(Time.timeScale - lastAppliedTimeScale) <= 0.025f)
                Time.timeScale = previousTimeScale;
            Time.fixedDeltaTime = previousFixedDeltaTime;
        }

        void Finish(int fired, int lethal)
        {
            RestoreTimeScale();
            active = false;
            if (canvas != null)
                canvas.gameObject.SetActive(false);
            ClearLocks();
            Finished?.Invoke(fired, lethal);
        }

        void EnsureHud()
        {
            if (canvas != null)
                return;
            GameObject root = new GameObject(
                "ChronoExecutionHud",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 555;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect = root.GetComponent<RectTransform>();

            GameObject overlayObject = new GameObject(
                "TemporalOverlay",
                typeof(RectTransform),
                typeof(Image));
            overlayObject.transform.SetParent(root.transform, false);
            RectTransform overlayRect =
                overlayObject.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            temporalOverlay = overlayObject.GetComponent<Image>();
            temporalOverlay.color = new Color(0f, 0f, 0f, 0.72f);
            temporalOverlay.raycastTarget = false;

            GameObject labelObject = new GameObject(
                "ChronoStatus",
                typeof(RectTransform),
                typeof(Text));
            labelObject.transform.SetParent(root.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.5f, 1f);
            labelRect.anchorMax = new Vector2(0.5f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.sizeDelta = new Vector2(900f, 72f);
            labelRect.anchoredPosition = new Vector2(0f, -72f);
            statusText = labelObject.GetComponent<Text>();
            statusText.font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            statusText.fontSize = 25;
            statusText.fontStyle = FontStyle.Bold;
            statusText.alignment = TextAnchor.MiddleCenter;
            statusText.color = new Color(0.5f, 0.94f, 1f);
            statusText.raycastTarget = false;
        }

        void UpdateHud(float elapsed)
        {
            if (canvas == null || targetCamera == null)
                return;
            float remaining = Mathf.Max(0f, TotalDuration - elapsed);
            statusText.text = elapsed < FreezeDuration
                ? "时间凝固"
                : "零时锁定  " + remaining.ToString("0.0") +
                  "s    可处决 " + FullyLockedCount + "/" +
                  TargetCount + "    再按技能键或左键提前处决";
            statusText.color = FullyLockedCount > 0
                ? new Color(1f, 0.72f, 0.2f)
                : new Color(0.5f, 0.94f, 1f);
            temporalOverlay.color = elapsed < FreezeDuration
                ? new Color(0f, 0f, 0f, 0.86f)
                : new Color(0f, 0f, 0f, 0.66f);

            foreach (TargetLock target in locks.Values)
            {
                if (target.MarkerRect == null || target.Root == null)
                    continue;
                Vector3 screen =
                    targetCamera.WorldToScreenPoint(target.AimPoint);
                bool onScreen = screen.z > 0f &&
                                screen.x >= -40f &&
                                screen.x <= Screen.width + 40f &&
                                screen.y >= -40f &&
                                screen.y <= Screen.height + 40f;
                target.MarkerRect.gameObject.SetActive(onScreen);
                if (!onScreen)
                    continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    screen,
                    null,
                    out Vector2 localPoint);
                target.MarkerRect.anchoredPosition = localPoint;
                target.MarkerGraphic.SetState(
                    target.Progress,
                    target.Visible,
                    target.Progress >= 0.999f);
            }
        }

        void CreateMarker(TargetLock target)
        {
            EnsureHud();
            GameObject marker = new GameObject(
                "ChronoTarget_" + target.Id,
                typeof(RectTransform),
                typeof(ChronoExecutionTargetGraphic));
            marker.transform.SetParent(canvas.transform, false);
            target.MarkerRect = marker.GetComponent<RectTransform>();
            target.MarkerRect.anchorMin = new Vector2(0.5f, 0.5f);
            target.MarkerRect.anchorMax = new Vector2(0.5f, 0.5f);
            target.MarkerRect.pivot = new Vector2(0.5f, 0.5f);
            target.MarkerRect.sizeDelta = new Vector2(112f, 112f);
            target.MarkerGraphic =
                marker.GetComponent<ChronoExecutionTargetGraphic>();
            target.MarkerGraphic.raycastTarget = false;
        }

        void RemoveLock(int id)
        {
            if (!locks.TryGetValue(id, out TargetLock target))
                return;
            if (target.MarkerRect != null)
                Destroy(target.MarkerRect.gameObject);
            locks.Remove(id);
        }

        void ClearLocks()
        {
            foreach (TargetLock target in locks.Values)
                if (target.MarkerRect != null)
                    Destroy(target.MarkerRect.gameObject);
            locks.Clear();
        }
    }

    public sealed class ChronoExecutionTargetGraphic : MaskableGraphic
    {
        float progress;
        bool targetVisible = true;
        bool locked;

        public void SetState(float value, bool visible, bool isLocked)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(progress, value) &&
                targetVisible == visible && locked == isLocked)
                return;
            progress = value;
            targetVisible = visible;
            locked = isLocked;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            Color32 baseColor = !targetVisible
                ? new Color(0.45f, 0.56f, 0.62f, 0.62f)
                : locked
                    ? new Color(1f, 0.58f, 0.08f, 1f)
                    : new Color(0.08f, 0.9f, 1f, 0.96f);
            const float radius = 45f;
            const float thickness = 3.2f;
            const int segments = 32;
            int drawn = Mathf.Max(1, Mathf.CeilToInt(segments * progress));
            for (int index = 0; index < drawn; index++)
            {
                float a0 = Mathf.PI * 0.5f -
                           index / (float)segments * Mathf.PI * 2f;
                float a1 = Mathf.PI * 0.5f -
                           (index + 0.78f) / segments * Mathf.PI * 2f;
                AddLine(
                    helper,
                    new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius,
                    new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius,
                    thickness,
                    baseColor);
            }

            float bracket = locked ? 27f : 22f;
            float outer = 53f;
            AddBracket(helper, new Vector2(-outer, outer), 1f, -1f,
                bracket, thickness, baseColor);
            AddBracket(helper, new Vector2(outer, outer), -1f, -1f,
                bracket, thickness, baseColor);
            AddBracket(helper, new Vector2(-outer, -outer), 1f, 1f,
                bracket, thickness, baseColor);
            AddBracket(helper, new Vector2(outer, -outer), -1f, 1f,
                bracket, thickness, baseColor);

            if (!locked)
                return;
            Vector2 top = new Vector2(0f, 16f);
            Vector2 right = new Vector2(13f, 0f);
            Vector2 bottom = new Vector2(0f, -16f);
            Vector2 left = new Vector2(-13f, 0f);
            AddLine(helper, top, right, 3.8f, baseColor);
            AddLine(helper, right, bottom, 3.8f, baseColor);
            AddLine(helper, bottom, left, 3.8f, baseColor);
            AddLine(helper, left, top, 3.8f, baseColor);
        }

        static void AddBracket(
            VertexHelper helper,
            Vector2 corner,
            float horizontalDirection,
            float verticalDirection,
            float length,
            float thickness,
            Color32 color)
        {
            AddLine(
                helper,
                corner,
                corner + Vector2.right * horizontalDirection * length,
                thickness,
                color);
            AddLine(
                helper,
                corner,
                corner + Vector2.up * verticalDirection * length,
                thickness,
                color);
        }

        static void AddLine(
            VertexHelper helper,
            Vector2 start,
            Vector2 end,
            float width,
            Color32 color)
        {
            Vector2 direction = end - start;
            if (direction.sqrMagnitude <= 0.0001f)
                return;
            Vector2 normal = new Vector2(-direction.y, direction.x)
                                 .normalized * width * 0.5f;
            int first = helper.currentVertCount;
            helper.AddVert(start - normal, color, Vector2.zero);
            helper.AddVert(start + normal, color, Vector2.up);
            helper.AddVert(end + normal, color, Vector2.one);
            helper.AddVert(end - normal, color, Vector2.right);
            helper.AddTriangle(first, first + 1, first + 2);
            helper.AddTriangle(first, first + 2, first + 3);
        }
    }

    public sealed class ChronoExecutionTracer : MonoBehaviour
    {
        LineRenderer line;
        Material material;
        float startedAt;
        const float Lifetime = 0.22f;

        public static void Spawn(Vector3 start, Vector3 end)
        {
            GameObject root = new GameObject("ChronoExecutionTracer");
            ChronoExecutionTracer tracer =
                root.AddComponent<ChronoExecutionTracer>();
            tracer.Initialize(start, end);
        }

        void Initialize(Vector3 start, Vector3 end)
        {
            startedAt = Time.unscaledTime;
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.widthMultiplier = 0.16f;
            line.numCapVertices = 4;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                material = new Material(shader);
                line.sharedMaterial = material;
            }
            line.startColor = new Color(0.3f, 0.95f, 1f, 1f);
            line.endColor = new Color(1f, 0.62f, 0.08f, 1f);
        }

        void Update()
        {
            float progress = Mathf.Clamp01(
                (Time.unscaledTime - startedAt) / Lifetime);
            if (progress >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            float alpha = 1f - progress;
            line.widthMultiplier = Mathf.Lerp(0.16f, 0.02f, progress);
            line.startColor = new Color(0.3f, 0.95f, 1f, alpha);
            line.endColor = new Color(1f, 0.62f, 0.08f, alpha);
        }

        void OnDestroy()
        {
            if (material != null)
                Destroy(material);
        }
    }
}
