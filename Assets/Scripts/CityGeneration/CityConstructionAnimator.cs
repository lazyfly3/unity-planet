using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace CityGeneration
{
    public enum CityConstructionPhase
    {
        Idle,
        Scanning,
        Supports,
        Platform,
        Roads,
        Blocks,
        Buildings,
        Completion
    }

    public sealed class CityConstructionItem
    {
        public int StableIndex { get; }
        public Vector2 Position { get; }
        public Func<GameObject> Factory { get; }
        public bool IsCreated { get; internal set; }
        public GameObject CreatedObject { get; internal set; }

        public CityConstructionItem(
            int stableIndex,
            Vector2 position,
            Func<GameObject> factory)
        {
            StableIndex = stableIndex;
            Position = position;
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }
    }

    public sealed class CityConstructionJob
    {
        public IReadOnlyList<Vector2> Boundary { get; }
        public Vector2 Center { get; }
        public float FoundationBottomHeight { get; }
        public float FoundationUndersideHeight { get; }
        public float PlatformTopHeight { get; }
        public Transform SpaceRoot { get; }
        public List<CityConstructionItem> Foundations { get; } =
            new List<CityConstructionItem>();
        public List<CityConstructionItem> Roads { get; } =
            new List<CityConstructionItem>();
        public List<CityConstructionItem> Blocks { get; } =
            new List<CityConstructionItem>();
        public List<CityConstructionItem> Buildings { get; } =
            new List<CityConstructionItem>();

        public CityConstructionJob(
            IReadOnlyList<Vector2> boundary,
            Vector2 center,
            float foundationBottomHeight,
            float foundationUndersideHeight,
            float platformTopHeight,
            Transform spaceRoot = null)
        {
            Boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
            Center = center;
            FoundationBottomHeight = foundationBottomHeight;
            FoundationUndersideHeight = foundationUndersideHeight;
            PlatformTopHeight = platformTopHeight;
            SpaceRoot = spaceRoot;
        }

        public Vector3 ToWorldPoint(Vector2 point, float height)
        {
            Vector3 local = new Vector3(point.x, height, point.y);
            return SpaceRoot != null
                ? SpaceRoot.TransformPoint(local)
                : local;
        }
    }

    public sealed class CityConstructionAnimator : MonoBehaviour
    {
        const string HologramShaderName =
            "CityGeneration/HolographicConstruction";
        static readonly int RevealProgressId =
            Shader.PropertyToID("_RevealProgress");
        static readonly int RevealModeId = Shader.PropertyToID("_RevealMode");
        static readonly int BuildMinYId = Shader.PropertyToID("_BuildMinY");
        static readonly int BuildMaxYId = Shader.PropertyToID("_BuildMaxY");
        static readonly int BuildOriginId = Shader.PropertyToID("_BuildOrigin");

        [Header("Timeline")]
        [SerializeField, Min(0.01f)] float scanDuration = 0.8f;
        [SerializeField, Min(0.01f)] float supportDuration = 1.7f;
        [SerializeField, Min(0.01f)] float platformDuration = 1.5f;
        [SerializeField, Min(0.01f)] float roadDuration = 1.8f;
        [SerializeField, Min(0.01f)] float blockDuration = 1f;
        [SerializeField, Min(0.01f)] float buildingDuration = 2.9f;
        [SerializeField, Min(0.01f)] float completionDuration = 0.3f;
        [SerializeField, Min(0.1f)] float deconstructionDuration = 1.6f;

        [Header("Frame Budget")]
        [SerializeField, Min(1)] int maximumBuildingsPerFrame = 4;
        [SerializeField, Min(1)] int maximumSimpleObjectsPerFrame = 24;
        [SerializeField, Min(0.1f)] float creationBudgetMilliseconds = 3f;
        [SerializeField, Min(0.05f)] float objectRevealDuration = 0.35f;
        [SerializeField, Min(0.05f)] float buildingRevealDuration = 0.7f;

        [Header("Effects")]
        [SerializeField] Transform effectsRoot;
        [SerializeField] Color hologramColor =
            new Color(0.05f, 0.9f, 1f, 1f);

        readonly List<RevealRecord> reveals = new List<RevealRecord>();
        MaterialPropertyBlock propertyBlock;

        CityConstructionJob currentJob;
        Coroutine routine;
        Material hologramMaterial;
        Material lineMaterial;
        Material particleMaterial;
        LineRenderer scanLine;
        ParticleSystem particles;
        Action<CityConstructionPhase, float, float> progressCallback;
        Action completionCallback;
        Action<float, float> deconstructionProgressCallback;
        Action deconstructionCompletionCallback;
        CityConstructionPhase phase;
        float constructionProgress;
        float deconstructionProgress;
        bool isDeconstructing;

        readonly List<RevealRecord> deconstructionBuildings =
            new List<RevealRecord>();
        readonly List<ScheduledReveal> deconstructionSurfaces =
            new List<ScheduledReveal>();
        readonly List<RevealRecord> deconstructionFoundations =
            new List<RevealRecord>();

        public bool IsRunning => routine != null;
        public bool IsDeconstructing => isDeconstructing;
        public float ConstructionProgress => constructionProgress;
        public float DeconstructionProgress => deconstructionProgress;
        public CityConstructionPhase CurrentPhase => phase;
        public float DeconstructionDuration => deconstructionDuration;
        public float TotalDuration =>
            scanDuration
            + supportDuration
            + platformDuration
            + roadDuration
            + blockDuration
            + buildingDuration
            + completionDuration;

        void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        public void Configure(Transform root)
        {
            effectsRoot = root;
        }

        public bool Begin(
            CityConstructionJob job,
            Action<CityConstructionPhase, float, float> onProgress,
            Action onCompleted)
        {
            if (job == null || IsRunning)
                return false;

            currentJob = job;
            progressCallback = onProgress;
            completionCallback = onCompleted;
            deconstructionProgressCallback = null;
            deconstructionCompletionCallback = null;
            constructionProgress = 0f;
            deconstructionProgress = 0f;
            isDeconstructing = false;
            phase = CityConstructionPhase.Scanning;
            SortItems(job.Foundations, job.Center);
            SortItems(job.Roads, job.Center);
            SortItems(job.Blocks, job.Center);
            SortItems(job.Buildings, job.Center);
            PrepareEffects();
            routine = StartCoroutine(Play());
            return true;
        }

        public void CompleteImmediately()
        {
            if (!IsRunning || currentJob == null)
                return;

            StopCoroutine(routine);
            routine = null;
            CreateRemaining(currentJob.Foundations, RevealKind.Height);
            CreateRemaining(currentJob.Roads, RevealKind.Dither);
            CreateRemaining(currentJob.Blocks, RevealKind.Dither);
            CreateRemaining(currentJob.Buildings, RevealKind.Height);
            FinishConstruction();
        }

        public bool BeginDeconstruction(
            CityConstructionJob job,
            Action<float, float> onProgress,
            Action onCompleted)
        {
            if (job == null || IsRunning)
                return false;

            currentJob = job;
            progressCallback = null;
            completionCallback = null;
            deconstructionProgressCallback = onProgress;
            deconstructionCompletionCallback = onCompleted;
            constructionProgress = 1f;
            deconstructionProgress = 0f;
            isDeconstructing = true;
            phase = CityConstructionPhase.Idle;
            reveals.Clear();
            deconstructionBuildings.Clear();
            deconstructionSurfaces.Clear();
            deconstructionFoundations.Clear();
            DisableJobColliders(job);
            PrepareEffects();
            ConfigureDeconstructionEffects();
            routine = StartCoroutine(PlayDeconstruction());
            return true;
        }

        public void CompleteDeconstructionImmediately()
        {
            if (!isDeconstructing || currentJob == null)
                return;
            if (routine != null)
                StopCoroutine(routine);
            routine = null;
            FinishDeconstruction();
        }

        public void Cancel()
        {
            if (routine != null)
                StopCoroutine(routine);
            routine = null;
            RestoreFinalAppearance();
            CleanupEffects();
            currentJob = null;
            progressCallback = null;
            completionCallback = null;
            deconstructionProgressCallback = null;
            deconstructionCompletionCallback = null;
            constructionProgress = 0f;
            deconstructionProgress = 0f;
            isDeconstructing = false;
            phase = CityConstructionPhase.Idle;
            deconstructionBuildings.Clear();
            deconstructionSurfaces.Clear();
            deconstructionFoundations.Clear();
        }

        public static IReadOnlyList<int> GetStableDistanceOrder(
            IReadOnlyList<Vector2> positions,
            Vector2 center)
        {
            if (positions == null)
                return Array.Empty<int>();

            return Enumerable.Range(0, positions.Count)
                .OrderBy(index => (positions[index] - center).sqrMagnitude)
                .ThenBy(index => index)
                .ToArray();
        }

        public static IReadOnlyList<int> GetStableDeconstructionOrder(
            IReadOnlyList<Vector2> positions,
            Vector2 center)
        {
            if (positions == null)
                return Array.Empty<int>();

            return Enumerable.Range(0, positions.Count)
                .OrderByDescending(
                    index => (positions[index] - center).sqrMagnitude)
                .ThenBy(index => index)
                .ToArray();
        }

        IEnumerator Play()
        {
            float offset = 0f;
            yield return RunTimedPhase(
                CityConstructionPhase.Scanning,
                offset,
                scanDuration,
                UpdateBoundaryScan);
            offset += scanDuration;

            CreateRemaining(currentJob.Foundations, RevealKind.ManualHeight);
            yield return RunTimedPhase(
                CityConstructionPhase.Supports,
                offset,
                supportDuration,
                progress =>
                {
                    SetFoundationRevealHeight(Mathf.Lerp(
                        currentJob.FoundationBottomHeight,
                        currentJob.FoundationUndersideHeight,
                        progress));
                    UpdateParticlePosition(
                        currentJob.Center,
                        Mathf.Lerp(
                            currentJob.FoundationBottomHeight,
                            currentJob.FoundationUndersideHeight,
                            progress));
                });
            offset += supportDuration;

            yield return RunTimedPhase(
                CityConstructionPhase.Platform,
                offset,
                platformDuration,
                progress =>
                {
                    SetFoundationRevealHeight(Mathf.Lerp(
                        currentJob.FoundationUndersideHeight,
                        currentJob.PlatformTopHeight + 0.05f,
                        progress));
                    UpdateParticlePosition(
                        currentJob.Center,
                        currentJob.PlatformTopHeight);
                });
            FinalizeManualReveals();
            offset += platformDuration;

            yield return RunBatchPhase(
                CityConstructionPhase.Roads,
                offset,
                roadDuration,
                currentJob.Roads,
                RevealKind.Dither,
                maximumSimpleObjectsPerFrame,
                objectRevealDuration);
            offset += roadDuration;

            yield return RunBatchPhase(
                CityConstructionPhase.Blocks,
                offset,
                blockDuration,
                currentJob.Blocks,
                RevealKind.Dither,
                maximumSimpleObjectsPerFrame,
                objectRevealDuration);
            offset += blockDuration;

            yield return RunBatchPhase(
                CityConstructionPhase.Buildings,
                offset,
                buildingDuration,
                currentJob.Buildings,
                RevealKind.Height,
                maximumBuildingsPerFrame,
                buildingRevealDuration);
            offset += buildingDuration;

            EmitCompletionBurst();
            yield return RunTimedPhase(
                CityConstructionPhase.Completion,
                offset,
                completionDuration,
                UpdateCompletionPulse);

            routine = null;
            FinishConstruction();
        }

        IEnumerator PlayDeconstruction()
        {
            float durationScale =
                deconstructionDuration / 1.6f;
            float buildingStart = 0.15f * durationScale;
            float buildingEnd = 0.85f * durationScale;
            float surfaceStart = 0.35f * durationScale;
            float surfaceEnd = 1.05f * durationScale;
            float foundationStart = 1.05f * durationScale;
            float foundationEnd = 1.5f * durationScale;
            float startedAt = Time.unscaledTime;
            bool buildingsRegistered = false;
            bool surfacesRegistered = false;
            bool foundationsRegistered = false;

            while (Time.unscaledTime - startedAt < deconstructionDuration)
            {
                float elapsed = Time.unscaledTime - startedAt;
                if (!buildingsRegistered && elapsed >= buildingStart)
                {
                    buildingsRegistered = true;
                    RegisterDeconstructionGroup(
                        currentJob.Buildings,
                        RevealKind.Height,
                        deconstructionBuildings);
                    EmitDeconstructionBurst(36);
                }
                if (!surfacesRegistered && elapsed >= surfaceStart)
                {
                    surfacesRegistered = true;
                    RegisterDeconstructionSurfaces(
                        currentJob.Roads,
                        currentJob.Blocks,
                        surfaceStart,
                        surfaceEnd);
                    EmitDeconstructionBurst(48);
                }
                if (!foundationsRegistered && elapsed >= foundationStart)
                {
                    foundationsRegistered = true;
                    RegisterDeconstructionGroup(
                        currentJob.Foundations,
                        RevealKind.Height,
                        deconstructionFoundations);
                    EmitDeconstructionBurst(60);
                }

                float buildingProgress = 1f - Mathf.InverseLerp(
                    buildingStart,
                    buildingEnd,
                    elapsed);
                for (int i = 0; i < deconstructionBuildings.Count; i++)
                {
                    ApplyReveal(
                        deconstructionBuildings[i],
                        buildingProgress);
                }

                for (int i = 0; i < deconstructionSurfaces.Count; i++)
                {
                    ScheduledReveal scheduled =
                        deconstructionSurfaces[i];
                    float progress = 1f - Mathf.Clamp01(
                        (elapsed - scheduled.Start)
                        / scheduled.Duration);
                    ApplyReveal(scheduled.Record, progress);
                }

                float foundationProgress = 1f - Mathf.InverseLerp(
                    foundationStart,
                    foundationEnd,
                    elapsed);
                for (int i = 0;
                     i < deconstructionFoundations.Count;
                     i++)
                {
                    ApplyReveal(
                        deconstructionFoundations[i],
                        foundationProgress);
                }

                deconstructionProgress = Mathf.Clamp01(
                    elapsed / deconstructionDuration);
                UpdateDeconstructionPulse(deconstructionProgress);
                deconstructionProgressCallback?.Invoke(
                    deconstructionProgress,
                    Mathf.Max(0f, deconstructionDuration - elapsed));
                yield return null;
            }

            routine = null;
            FinishDeconstruction();
        }

        void RegisterDeconstructionGroup(
            IReadOnlyList<CityConstructionItem> items,
            RevealKind kind,
            List<RevealRecord> destination)
        {
            for (int i = 0; i < items.Count; i++)
            {
                GameObject target = items[i].CreatedObject;
                if (!items[i].IsCreated || target == null)
                    continue;
                RevealRecord record = RegisterReveal(
                    target,
                    kind,
                    deconstructionDuration,
                    1f);
                if (record == null)
                    continue;
                destination.Add(record);
                reveals.Add(record);
            }
        }

        void RegisterDeconstructionSurfaces(
            IReadOnlyList<CityConstructionItem> roads,
            IReadOnlyList<CityConstructionItem> blocks,
            float phaseStart,
            float phaseEnd)
        {
            var items = new List<CityConstructionItem>(
                roads.Count + blocks.Count);
            for (int i = 0; i < roads.Count; i++)
            {
                if (roads[i].IsCreated && roads[i].CreatedObject != null)
                    items.Add(roads[i]);
            }
            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i].IsCreated && blocks[i].CreatedObject != null)
                    items.Add(blocks[i]);
            }
            items.Sort((left, right) =>
            {
                float leftDistance =
                    (left.Position - currentJob.Center).sqrMagnitude;
                float rightDistance =
                    (right.Position - currentJob.Center).sqrMagnitude;
                int distance = rightDistance.CompareTo(leftDistance);
                return distance != 0
                    ? distance
                    : left.StableIndex.CompareTo(right.StableIndex);
            });

            float fadeDuration = Mathf.Min(
                0.32f * deconstructionDuration / 1.6f,
                phaseEnd - phaseStart);
            float staggerWindow = Mathf.Max(
                0f,
                phaseEnd - phaseStart - fadeDuration);
            for (int i = 0; i < items.Count; i++)
            {
                RevealRecord record = RegisterReveal(
                    items[i].CreatedObject,
                    RevealKind.Dither,
                    deconstructionDuration,
                    1f);
                if (record == null)
                    continue;
                float normalizedIndex = items.Count <= 1
                    ? 0f
                    : i / (float)(items.Count - 1);
                deconstructionSurfaces.Add(new ScheduledReveal(
                    record,
                    phaseStart + staggerWindow * normalizedIndex,
                    fadeDuration));
                reveals.Add(record);
            }
        }

        IEnumerator RunTimedPhase(
            CityConstructionPhase currentPhase,
            float timelineOffset,
            float duration,
            Action<float> update)
        {
            float startedAt = Time.unscaledTime;
            float progress = 0f;
            while (progress < 1f)
            {
                progress = Mathf.Clamp01(
                    (Time.unscaledTime - startedAt) / duration);
                phase = currentPhase;
                update?.Invoke(progress);
                UpdateAutomaticReveals();
                ReportProgress(timelineOffset + duration * progress);
                yield return null;
            }
        }

        IEnumerator RunBatchPhase(
            CityConstructionPhase currentPhase,
            float timelineOffset,
            float duration,
            List<CityConstructionItem> items,
            RevealKind revealKind,
            int maximumPerFrame,
            float revealDuration)
        {
            float startedAt = Time.unscaledTime;
            float creationWindow = Mathf.Max(
                0.05f,
                duration - revealDuration);
            int created = 0;

            while (Time.unscaledTime - startedAt < duration)
            {
                float elapsed = Time.unscaledTime - startedAt;
                float phaseProgress = Mathf.Clamp01(elapsed / duration);
                int targetCount = Mathf.Min(
                    items.Count,
                    Mathf.CeilToInt(
                        items.Count
                        * Mathf.Clamp01(elapsed / creationWindow)));
                created += CreateDueItems(
                    items,
                    created,
                    targetCount,
                    maximumPerFrame,
                    revealKind,
                    revealDuration);
                phase = currentPhase;
                UpdateAutomaticReveals();
                ReportProgress(timelineOffset + duration * phaseProgress);
                yield return null;
            }

            CreateRemaining(items, revealKind, revealDuration);
            UpdateAutomaticReveals(true);
            ReportProgress(timelineOffset + duration);
        }

        int CreateDueItems(
            List<CityConstructionItem> items,
            int startIndex,
            int targetCount,
            int maximumPerFrame,
            RevealKind revealKind,
            float revealDuration)
        {
            long startedAt = Stopwatch.GetTimestamp();
            int created = 0;
            for (int i = startIndex;
                 i < targetCount && created < maximumPerFrame;
                 i++)
            {
                CreateItem(items[i], revealKind, revealDuration);
                created++;
                if (ElapsedMilliseconds(startedAt)
                    >= creationBudgetMilliseconds)
                {
                    break;
                }
            }
            return created;
        }

        void CreateRemaining(
            List<CityConstructionItem> items,
            RevealKind revealKind,
            float revealDuration = 0f)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].IsCreated)
                    CreateItem(items[i], revealKind, revealDuration);
            }
        }

        void CreateItem(
            CityConstructionItem item,
            RevealKind revealKind,
            float revealDuration)
        {
            if (item.IsCreated)
                return;

            GameObject created = item.Factory();
            item.IsCreated = true;
            item.CreatedObject = created;
            if (created == null)
                return;

            RevealRecord record = RegisterReveal(
                created,
                revealKind,
                revealDuration);
            if (record != null)
            {
                reveals.Add(record);
                UpdateParticlePosition(
                    item.Position,
                    record.Bounds.center.y);
            }
        }

        RevealRecord RegisterReveal(
            GameObject target,
            RevealKind revealKind,
            float revealDuration,
            float initialProgress = 0f)
        {
            Renderer[] renderers =
                target.GetComponentsInChildren<Renderer>(true);
            Collider[] colliders =
                target.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;

            if (renderers.Length == 0)
            {
                return new RevealRecord(
                    renderers,
                    Array.Empty<Material[]>(),
                    colliders,
                    new Bounds(target.transform.position, Vector3.zero),
                    revealKind,
                    Time.unscaledTime,
                    revealDuration);
            }

            EnsureHologramMaterial();
            var originalMaterials = new Material[renderers.Length][];
            Bounds bounds = renderers[0].bounds;
            for (int i = 0; i < renderers.Length; i++)
            {
                originalMaterials[i] = renderers[i].sharedMaterials;
                int materialCount = Mathf.Max(
                    1,
                    originalMaterials[i].Length);
                var hologramMaterials = new Material[materialCount];
                for (int slot = 0; slot < materialCount; slot++)
                    hologramMaterials[slot] = hologramMaterial;
                renderers[i].sharedMaterials = hologramMaterials;
                bounds.Encapsulate(renderers[i].bounds);
            }

            var record = new RevealRecord(
                renderers,
                originalMaterials,
                colliders,
                bounds,
                revealKind,
                Time.unscaledTime,
                revealDuration);
            ApplyReveal(record, initialProgress);
            return record;
        }

        void SetFoundationRevealHeight(float localHeight)
        {
            float worldHeight = currentJob.ToWorldPoint(
                currentJob.Center,
                localHeight).y;
            for (int i = 0; i < reveals.Count; i++)
            {
                RevealRecord record = reveals[i];
                if (record.Kind != RevealKind.ManualHeight)
                    continue;
                float progress = Mathf.InverseLerp(
                    record.Bounds.min.y - 0.05f,
                    record.Bounds.max.y + 0.05f,
                    worldHeight);
                ApplyReveal(record, progress);
            }
        }

        void UpdateAutomaticReveals(bool forceComplete = false)
        {
            for (int i = 0; i < reveals.Count; i++)
            {
                RevealRecord record = reveals[i];
                if (record.Kind == RevealKind.ManualHeight)
                    continue;
                float progress = forceComplete
                    ? 1f
                    : Mathf.Clamp01(
                        (Time.unscaledTime - record.StartedAt)
                        / Mathf.Max(0.01f, record.Duration));
                ApplyReveal(record, progress);
                if (progress >= 1f)
                    RestoreAppearance(record, false);
            }
        }

        void ApplyReveal(RevealRecord record, float progress)
        {
            if (record.AppearanceRestored)
                return;
            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();
            float mode = record.Kind == RevealKind.Dither ? 1f : 0f;
            for (int i = 0; i < record.Renderers.Length; i++)
            {
                Renderer renderer = record.Renderers[i];
                if (renderer == null)
                    continue;
                propertyBlock.Clear();
                propertyBlock.SetFloat(RevealProgressId, progress);
                propertyBlock.SetFloat(RevealModeId, mode);
                propertyBlock.SetFloat(BuildMinYId, record.Bounds.min.y);
                propertyBlock.SetFloat(BuildMaxYId, record.Bounds.max.y);
                propertyBlock.SetVector(
                    BuildOriginId,
                    currentJob.ToWorldPoint(
                        currentJob.Center,
                        currentJob.PlatformTopHeight));
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        void FinishConstruction()
        {
            CreateRemaining(currentJob.Foundations, RevealKind.Height);
            CreateRemaining(currentJob.Roads, RevealKind.Dither);
            CreateRemaining(currentJob.Blocks, RevealKind.Dither);
            CreateRemaining(currentJob.Buildings, RevealKind.Height);
            RestoreFinalAppearance();
            CleanupEffects();
            constructionProgress = 1f;
            phase = CityConstructionPhase.Idle;
            progressCallback?.Invoke(
                CityConstructionPhase.Completion,
                1f,
                0f);
            Action callback = completionCallback;
            currentJob = null;
            progressCallback = null;
            completionCallback = null;
            callback?.Invoke();
        }

        void FinishDeconstruction()
        {
            SetJobRenderersEnabled(currentJob, false);
            for (int recordIndex = 0;
                 recordIndex < reveals.Count;
                 recordIndex++)
            {
                RevealRecord record = reveals[recordIndex];
                for (int rendererIndex = 0;
                     rendererIndex < record.Renderers.Length;
                     rendererIndex++)
                {
                    if (record.Renderers[rendererIndex] != null)
                        record.Renderers[rendererIndex].enabled = false;
                }
            }

            reveals.Clear();
            deconstructionBuildings.Clear();
            deconstructionSurfaces.Clear();
            deconstructionFoundations.Clear();
            CleanupEffects();
            deconstructionProgress = 1f;
            deconstructionProgressCallback?.Invoke(1f, 0f);
            Action callback = deconstructionCompletionCallback;
            currentJob = null;
            progressCallback = null;
            completionCallback = null;
            deconstructionProgressCallback = null;
            deconstructionCompletionCallback = null;
            isDeconstructing = false;
            phase = CityConstructionPhase.Idle;
            callback?.Invoke();
        }

        static void SetJobRenderersEnabled(
            CityConstructionJob job,
            bool enabled)
        {
            if (job == null)
                return;

            SetEnabled(job.Foundations);
            SetEnabled(job.Roads);
            SetEnabled(job.Blocks);
            SetEnabled(job.Buildings);

            void SetEnabled(
                IReadOnlyList<CityConstructionItem> items)
            {
                for (int itemIndex = 0;
                     itemIndex < items.Count;
                     itemIndex++)
                {
                    GameObject target = items[itemIndex].CreatedObject;
                    if (!items[itemIndex].IsCreated || target == null)
                        continue;
                    Renderer[] renderers =
                        target.GetComponentsInChildren<Renderer>(true);
                    for (int rendererIndex = 0;
                         rendererIndex < renderers.Length;
                         rendererIndex++)
                    {
                        renderers[rendererIndex].enabled = enabled;
                    }
                }
            }
        }

        static void DisableJobColliders(CityConstructionJob job)
        {
            Disable(job.Foundations);
            Disable(job.Roads);
            Disable(job.Blocks);
            Disable(job.Buildings);

            static void Disable(
                IReadOnlyList<CityConstructionItem> items)
            {
                for (int itemIndex = 0;
                     itemIndex < items.Count;
                     itemIndex++)
                {
                    GameObject target = items[itemIndex].CreatedObject;
                    if (!items[itemIndex].IsCreated || target == null)
                        continue;
                    Collider[] colliders =
                        target.GetComponentsInChildren<Collider>(true);
                    for (int colliderIndex = 0;
                         colliderIndex < colliders.Length;
                         colliderIndex++)
                    {
                        colliders[colliderIndex].enabled = false;
                    }
                }
            }
        }

        void RestoreFinalAppearance()
        {
            for (int recordIndex = 0;
                 recordIndex < reveals.Count;
                 recordIndex++)
            {
                RevealRecord record = reveals[recordIndex];
                RestoreAppearance(record, true);
            }
            reveals.Clear();
        }

        void FinalizeManualReveals()
        {
            for (int i = 0; i < reveals.Count; i++)
            {
                if (reveals[i].Kind == RevealKind.ManualHeight)
                    RestoreAppearance(reveals[i], false);
            }
        }

        static void RestoreAppearance(
            RevealRecord record,
            bool enableColliders)
        {
            if (!record.AppearanceRestored)
            {
                for (int i = 0; i < record.Renderers.Length; i++)
                {
                    if (record.Renderers[i] == null)
                        continue;
                    record.Renderers[i].sharedMaterials =
                        record.OriginalMaterials[i];
                    record.Renderers[i].SetPropertyBlock(null);
                }
                record.AppearanceRestored = true;
            }

            if (!enableColliders)
                return;
            for (int i = 0; i < record.Colliders.Length; i++)
            {
                if (record.Colliders[i] != null)
                    record.Colliders[i].enabled = true;
            }
        }

        void ReportProgress(float elapsed)
        {
            constructionProgress = Mathf.Clamp01(
                elapsed / Mathf.Max(0.01f, TotalDuration));
            progressCallback?.Invoke(
                phase,
                constructionProgress,
                Mathf.Max(0f, TotalDuration - elapsed));
        }

        void PrepareEffects()
        {
            CleanupEffects();
            Transform root = effectsRoot != null ? effectsRoot : transform;

            var scanObject = new GameObject("ConstructionScanLine");
            scanObject.transform.SetParent(root, false);
            scanLine = scanObject.AddComponent<LineRenderer>();
            scanLine.useWorldSpace = true;
            scanLine.widthMultiplier = 0.55f;
            scanLine.numCapVertices = 4;
            scanLine.numCornerVertices = 2;
            scanLine.positionCount = 0;
            scanLine.startColor = hologramColor;
            scanLine.endColor = new Color(
                hologramColor.r,
                hologramColor.g,
                hologramColor.b,
                0.05f);
            Shader lineShader = Shader.Find("Sprites/Default");
            if (lineShader == null)
                lineShader = Shader.Find("Unlit/Color");
            if (lineShader != null)
            {
                lineMaterial = new Material(lineShader)
                {
                    name = "City Construction Line"
                };
                scanLine.sharedMaterial = lineMaterial;
            }

            var particleObject = new GameObject("ConstructionParticles");
            particleObject.transform.SetParent(root, false);
            particles = particleObject.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = true;
            main.startLifetime = 0.45f;
            main.startSpeed = 0.8f;
            main.startSize = 0.22f;
            main.startColor = hologramColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 220;
            var emission = particles.emission;
            emission.rateOverTime = 45f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.75f;
            var particleRenderer =
                particleObject.GetComponent<ParticleSystemRenderer>();
            Shader particleShader = Shader.Find(
                "Particles/Standard Unlit");
            if (particleShader == null)
                particleShader = Shader.Find("Particles/Additive");
            if (particleShader != null)
            {
                particleMaterial = new Material(particleShader)
                {
                    name = "City Construction Particles"
                };
                if (particleMaterial.HasProperty("_Color"))
                    particleMaterial.SetColor("_Color", hologramColor);
                particleRenderer.sharedMaterial = particleMaterial;
            }
            particles.Play();
        }

        void ConfigureDeconstructionEffects()
        {
            if (currentJob == null)
                return;
            float radius = GetMaximumCityRadius();
            if (particles != null)
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.loop = true;
                main.startLifetime = 0.72f;
                main.startSpeed = -Mathf.Max(2f, radius / 0.72f);
                main.startSize = 0.24f;
                main.maxParticles = 360;
                var emission = particles.emission;
                emission.rateOverTime = 110f;
                var shape = particles.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = radius;
                shape.radiusThickness = 0.08f;
                particles.transform.position = currentJob.ToWorldPoint(
                    currentJob.Center,
                    currentJob.PlatformTopHeight + 1f);
                particles.Play();
            }
            UpdateDeconstructionPulse(0f);
        }

        void UpdateDeconstructionPulse(float progress)
        {
            if (scanLine == null || currentJob == null)
                return;

            const int segments = 64;
            float ringProgress = Mathf.Clamp01(
                (progress - 0.08f) / 0.92f);
            float radius = GetMaximumCityRadius()
                * (1f - ringProgress);
            scanLine.loop = true;
            scanLine.positionCount = segments;
            scanLine.widthMultiplier = Mathf.Lerp(
                0.85f,
                0.2f,
                ringProgress);
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                scanLine.SetPosition(
                    i,
                    currentJob.ToWorldPoint(
                        new Vector2(
                            currentJob.Center.x
                                + Mathf.Cos(angle) * radius,
                            currentJob.Center.y
                                + Mathf.Sin(angle) * radius),
                        currentJob.PlatformTopHeight + 0.9f));
            }
        }

        float GetMaximumCityRadius()
        {
            float radius = 1f;
            if (currentJob == null || currentJob.Boundary == null)
                return radius;
            for (int i = 0; i < currentJob.Boundary.Count; i++)
            {
                radius = Mathf.Max(
                    radius,
                    Vector2.Distance(
                        currentJob.Center,
                        currentJob.Boundary[i]));
            }
            return radius;
        }

        void EmitDeconstructionBurst(int count)
        {
            if (particles != null)
                particles.Emit(Mathf.Max(1, count));
        }

        void UpdateBoundaryScan(float progress)
        {
            if (scanLine == null
                || currentJob.Boundary == null
                || currentJob.Boundary.Count < 2)
            {
                return;
            }

            const int trailSamples = 18;
            scanLine.loop = false;
            scanLine.positionCount = trailSamples;
            float total = currentJob.Boundary.Count;
            for (int i = 0; i < trailSamples; i++)
            {
                float sampleProgress = Mathf.Clamp01(
                    progress - (trailSamples - 1 - i) * 0.006f);
                float edgePosition = sampleProgress * total;
                int edge = Mathf.FloorToInt(edgePosition)
                    % currentJob.Boundary.Count;
                int next = (edge + 1) % currentJob.Boundary.Count;
                float edgeProgress = edgePosition - Mathf.Floor(edgePosition);
                Vector2 point = Vector2.Lerp(
                    currentJob.Boundary[edge],
                    currentJob.Boundary[next],
                    edgeProgress);
                scanLine.SetPosition(
                    i,
                    currentJob.ToWorldPoint(
                        point,
                        currentJob.PlatformTopHeight + 0.8f));
            }
            UpdateParticlePosition(
                currentJob.Center,
                currentJob.PlatformTopHeight + 0.8f);
        }

        void UpdateCompletionPulse(float progress)
        {
            if (scanLine == null)
                return;

            const int segments = 64;
            scanLine.loop = true;
            scanLine.positionCount = segments;
            float maximumRadius = 1f;
            for (int i = 0; i < currentJob.Boundary.Count; i++)
            {
                maximumRadius = Mathf.Max(
                    maximumRadius,
                    Vector2.Distance(
                        currentJob.Center,
                        currentJob.Boundary[i]));
            }
            float radius = maximumRadius * progress;
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                scanLine.SetPosition(
                    i,
                    currentJob.ToWorldPoint(
                        new Vector2(
                            currentJob.Center.x
                                + Mathf.Cos(angle) * radius,
                            currentJob.Center.y
                                + Mathf.Sin(angle) * radius),
                        currentJob.PlatformTopHeight + 0.9f));
            }
        }

        void UpdateParticlePosition(Vector2 point, float height)
        {
            if (particles != null)
            {
                particles.transform.position =
                    currentJob.ToWorldPoint(point, height);
            }
        }

        void EmitCompletionBurst()
        {
            if (particles == null)
                return;
            var emission = particles.emission;
            emission.rateOverTime = 0f;
            particles.transform.position =
                currentJob.ToWorldPoint(
                    currentJob.Center,
                    currentJob.PlatformTopHeight + 1f);
            particles.Emit(100);
        }

        void CleanupEffects()
        {
            if (scanLine != null)
                DestroySafely(scanLine.gameObject);
            if (particles != null)
                DestroySafely(particles.gameObject);
            if (lineMaterial != null)
                DestroySafely(lineMaterial);
            if (particleMaterial != null)
                DestroySafely(particleMaterial);
            scanLine = null;
            particles = null;
            lineMaterial = null;
            particleMaterial = null;
        }

        void EnsureHologramMaterial()
        {
            if (hologramMaterial != null)
                return;
            Shader shader = Shader.Find(HologramShaderName);
            if (shader == null)
            {
                UnityEngine.Debug.LogError(
                    "Missing shader: " + HologramShaderName);
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
                return;
            hologramMaterial = new Material(shader)
            {
                name = "City Holographic Construction"
            };
            if (hologramMaterial.HasProperty("_HologramColor"))
            {
                hologramMaterial.SetColor(
                    "_HologramColor",
                    hologramColor);
            }
            hologramMaterial.enableInstancing = true;
        }

        static void SortItems(
            List<CityConstructionItem> items,
            Vector2 center)
        {
            items.Sort((left, right) =>
            {
                float leftDistance =
                    (left.Position - center).sqrMagnitude;
                float rightDistance =
                    (right.Position - center).sqrMagnitude;
                int distanceComparison =
                    leftDistance.CompareTo(rightDistance);
                return distanceComparison != 0
                    ? distanceComparison
                    : left.StableIndex.CompareTo(right.StableIndex);
            });
        }

        static double ElapsedMilliseconds(long startedAt)
        {
            return (Stopwatch.GetTimestamp() - startedAt)
                   * 1000.0
                   / Stopwatch.Frequency;
        }

        void OnDestroy()
        {
            Cancel();
            if (hologramMaterial != null)
                DestroySafely(hologramMaterial);
        }

        static void DestroySafely(UnityEngine.Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        enum RevealKind
        {
            Height,
            ManualHeight,
            Dither
        }

        readonly struct ScheduledReveal
        {
            public readonly RevealRecord Record;
            public readonly float Start;
            public readonly float Duration;

            public ScheduledReveal(
                RevealRecord record,
                float start,
                float duration)
            {
                Record = record;
                Start = start;
                Duration = Mathf.Max(0.01f, duration);
            }
        }

        sealed class RevealRecord
        {
            public Renderer[] Renderers { get; }
            public Material[][] OriginalMaterials { get; }
            public Collider[] Colliders { get; }
            public Bounds Bounds { get; }
            public RevealKind Kind { get; }
            public float StartedAt { get; }
            public float Duration { get; }
            public bool AppearanceRestored { get; set; }

            public RevealRecord(
                Renderer[] renderers,
                Material[][] originalMaterials,
                Collider[] colliders,
                Bounds bounds,
                RevealKind kind,
                float startedAt,
                float duration)
            {
                Renderers = renderers;
                OriginalMaterials = originalMaterials;
                Colliders = colliders;
                Bounds = bounds;
                Kind = kind;
                StartedAt = startedAt;
                Duration = Mathf.Max(0.01f, duration);
            }
        }
    }
}
