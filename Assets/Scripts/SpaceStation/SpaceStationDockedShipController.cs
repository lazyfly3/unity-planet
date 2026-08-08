using System;
using System.Collections;
using System.Collections.Generic;
using ModularAssembly;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.SpaceStation
{
    [DisallowMultipleComponent]
    public sealed class SpaceStationDockedShipController : MonoBehaviour
    {
        const string DefinitionResourcePath =
            "ModularAssembly/Definitions";
        const string CoreSourceId =
            "block:core:core_heavy_222";
        const string CoreVisualName = "NeoXCoreHeavy222";

        [SerializeField] Transform stationWalker;
        [SerializeField] GameObject defaultParkedShip;
        [SerializeField] string assemblySceneName =
            "ModularAssemblyLab";
        [SerializeField, Min(1f)] float interactionDistance = 3.4f;
        [SerializeField] Vector3 floor01SpawnPosition =
            new Vector3(2f, 0.08f, 5f);
        [SerializeField] Vector3 dockingBaySpawnPosition =
            new Vector3(-20.54f, 0.08f, 9.2f);

        ModularContentService contentService;
        GameObject savedVisualRoot;
        bool transitionStarted;
        bool loadingSavedDesign;
        bool usingSavedDesign;
        string statusMessage = string.Empty;
        GUIStyle promptStyle;
        GUIStyle statusStyle;

        public bool IsUsingSavedDesign => usingSavedDesign;
        public bool IsLoadingSavedDesign => loadingSavedDesign;
        public string StatusMessage => statusMessage;
        public SpaceStationSpawnLocation LastAppliedSpawnLocation
        {
            get;
            private set;
        }

        public void Configure(
            Transform walker,
            GameObject fallbackShip,
            Vector3 floorRoomSpawn,
            Vector3 dockRoomSpawn,
            string targetAssemblyScene = "ModularAssemblyLab")
        {
            stationWalker = walker;
            defaultParkedShip = fallbackShip;
            HideDefaultParkedShip();
            floor01SpawnPosition = floorRoomSpawn;
            dockingBaySpawnPosition = dockRoomSpawn;
            assemblySceneName = string.IsNullOrWhiteSpace(
                targetAssemblyScene)
                ? "ModularAssemblyLab"
                : targetAssemblyScene;
        }

        void Awake()
        {
            HideDefaultParkedShip();
            LastAppliedSpawnLocation =
                SpaceStationFlowContext.EnterStation();
            ApplyEntrySpawn(LastAppliedSpawnLocation);
        }

        void ApplyEntrySpawn(
            SpaceStationSpawnLocation spawnLocation)
        {
            if (stationWalker == null)
            {
                return;
            }

            CharacterController character =
                stationWalker.GetComponent<CharacterController>();
            bool wasEnabled = character != null && character.enabled;
            if (wasEnabled)
            {
                character.enabled = false;
            }

            Vector3 position = spawnLocation ==
                               SpaceStationSpawnLocation.DockingBay
                ? dockingBaySpawnPosition
                : floor01SpawnPosition;
            stationWalker.position = position;
            Vector3 lookDirection = transform.position - position;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude > 0.01f)
            {
                stationWalker.rotation = Quaternion.LookRotation(
                    lookDirection.normalized,
                    Vector3.up);
            }

            if (wasEnabled)
            {
                character.enabled = true;
            }
        }

        IEnumerator Start()
        {
            if (stationWalker == null)
            {
                SpaceStationFirstPersonController walker =
                    FindObjectOfType<
                        SpaceStationFirstPersonController>();
                stationWalker = walker != null
                    ? walker.transform
                    : null;
            }

            if (string.IsNullOrWhiteSpace(
                    GalaxyLaunchContext.SelectedSlotId))
            {
                statusMessage =
                    "未选择正式存档，停靠位保持为空。";
                yield break;
            }

            yield return BuildSavedDesign();
        }

        void Update()
        {
            if (!Input.GetKeyDown(KeyCode.F))
            {
                return;
            }

            TryEnterAssembly();
        }

        public bool TryEnterAssembly()
        {
            if (transitionStarted ||
                !IsWalkerInInteractionRange())
            {
                return false;
            }

            transitionStarted = true;
            SpaceStationFlowContext.BeginAssemblyFromStation();
            SceneManager.LoadScene(
                assemblySceneName,
                LoadSceneMode.Single);
            return true;
        }

        IEnumerator BuildSavedDesign()
        {
            loadingSavedDesign = true;
            var store = new ModularBlueprintStore();
            if (!store.TryLoad(
                    out ModularBlueprintData blueprint,
                    out string loadError))
            {
                loadingSavedDesign = false;
                if (string.IsNullOrWhiteSpace(loadError))
                {
                    statusMessage =
                        "当前存档还没有模块飞船，停靠位保持为空。";
                }
                else
                {
                    statusMessage =
                        "停靠飞船蓝图读取失败：" + loadError;
                    Debug.LogError(
                        "[SpaceStation] " + statusMessage,
                        this);
                }
                yield break;
            }

            contentService = GetComponent<ModularContentService>() ??
                             gameObject.AddComponent<
                                 ModularContentService>();
            yield return contentService.Initialize();
            if (contentService.Catalog == null ||
                contentService.Catalog.Count == 0)
            {
                FailSavedDesign(
                    "模块内容目录不可用：" +
                    (contentService.LastError ?? "目录为空。"));
                yield break;
            }

            GridModuleDefinition[] baseDefinitions =
                Resources.LoadAll<GridModuleDefinition>(
                    DefinitionResourcePath);
            if (baseDefinitions == null ||
                baseDefinitions.Length == 0)
            {
                FailSavedDesign(
                    "运行时模块定义资源缺失，停靠位保持为空。");
                yield break;
            }

            var model = new GridAssemblyModel(baseDefinitions);
            IReadOnlyDictionary<string, ModularContentRecord> records =
                NeoXCatalogIntegration.RegisterDefinitions(
                    model,
                    contentService.Catalog);
            if (!model.RestoreBlueprint(
                    blueprint,
                    out string restoreError))
            {
                FailSavedDesign(
                    "已保存模块飞船无法恢复：" + restoreError);
                yield break;
            }

            savedVisualRoot = new GameObject(
                "ParkedSavedModularShip_Static");
            savedVisualRoot.transform.SetParent(transform, false);
            savedVisualRoot.transform.localPosition = Vector3.zero;
            savedVisualRoot.transform.localRotation = Quaternion.identity;
            savedVisualRoot.transform.localScale = Vector3.one;

            Transform core = new GameObject("CoreVisual").transform;
            core.SetParent(savedVisualRoot.transform, false);
            GridAssemblyPresenter presenter =
                savedVisualRoot.AddComponent<GridAssemblyPresenter>();
            presenter.Initialize(model, null, core);

            NeoXCatalogIntegration integration =
                savedVisualRoot.AddComponent<
                    NeoXCatalogIntegration>();
            integration.InitializeRuntime(
                contentService,
                presenter,
                records);

            const int maximumWaitFrames = 1800;
            int waitFrames = 0;
            while ((!integration.IsReady ||
                    contentService.IsLoadingAssets) &&
                   waitFrames < maximumWaitFrames)
            {
                waitFrames++;
                yield return null;
            }

            if (!integration.IsReady ||
                contentService.IsLoadingAssets)
            {
                FailSavedDesign(
                    "模块外观载入超时，停靠位保持为空。");
                yield break;
            }

            string coreVisualError = null;
            yield return ReplaceCoreVisual(
                presenter,
                error => coreVisualError = error);
            if (!string.IsNullOrEmpty(coreVisualError))
            {
                FailSavedDesign(coreVisualError);
                yield break;
            }

            yield return null;
            if (!TryPrepareStaticVisual(
                    savedVisualRoot,
                    out string visualError))
            {
                FailSavedDesign(visualError);
                yield break;
            }

            HideDefaultParkedShip();

            usingSavedDesign = true;
            loadingSavedDesign = false;
            statusMessage = "停靠位已同步最后一次成功保存的模块飞船。";
        }

        IEnumerator ReplaceCoreVisual(
            GridAssemblyPresenter presenter,
            Action<string> completed)
        {
            GridModuleView coreView = null;
            foreach (GridModuleView view in presenter.Views.Values)
            {
                if (view?.Record?.Definition == null ||
                    !string.Equals(
                        view.Record.Definition.ModuleId,
                        GridAssemblyModel.CoreModuleId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                coreView = view;
                break;
            }

            if (coreView == null)
            {
                completed(
                    "停靠飞船缺少驾驶核心外观，停靠位保持为空。");
                yield break;
            }

            ModularContentRecord coreRecord = null;
            foreach (ModularContentRecord record in
                     contentService.Catalog.Items)
            {
                if (record != null &&
                    string.Equals(
                        record.sourceId,
                        CoreSourceId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    coreRecord = record;
                    break;
                }
            }

            if (coreRecord == null)
            {
                completed(
                    "模块目录缺少驾驶核心模型，停靠位保持为空。");
                yield break;
            }

            GameObject loaded = null;
            if (!contentService.TryInstantiatePrepared(
                    coreRecord,
                    coreView.transform,
                    out loaded))
            {
                yield return contentService.InstantiateAsync(
                    coreRecord,
                    coreView.transform,
                    value => loaded = value);
            }

            if (coreView == null || loaded == null)
            {
                if (loaded != null)
                {
                    Destroy(loaded);
                }
                completed(
                    "驾驶核心模型载入失败，停靠位保持为空。");
                yield break;
            }

            loaded.name = CoreVisualName;
            foreach (Collider collider in
                     loaded.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
            foreach (Renderer renderer in
                     coreView.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.transform.IsChildOf(loaded.transform))
                {
                    renderer.enabled = false;
                }
            }

            completed(string.Empty);
        }

        bool TryPrepareStaticVisual(
            GameObject visualRoot,
            out string error)
        {
            if (!TryCalculateRendererBounds(
                    visualRoot,
                    out Bounds initialBounds))
            {
                error =
                    "模块飞船没有可显示的渲染网格，停靠位保持为空。";
                return false;
            }

            foreach (Rigidbody body in
                     visualRoot.GetComponentsInChildren<
                         Rigidbody>(true))
            {
                body.detectCollisions = false;
                body.isKinematic = true;
                Destroy(body);
            }
            foreach (Joint joint in
                     visualRoot.GetComponentsInChildren<Joint>(true))
            {
                Destroy(joint);
            }
            foreach (Collider collider in
                     visualRoot.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                Destroy(collider);
            }
            foreach (ParticleSystem particles in
                     visualRoot.GetComponentsInChildren<
                         ParticleSystem>(true))
            {
                particles.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            foreach (AudioSource audioSource in
                     visualRoot.GetComponentsInChildren<
                         AudioSource>(true))
            {
                audioSource.Stop();
                audioSource.enabled = false;
            }
            foreach (Light light in
                     visualRoot.GetComponentsInChildren<Light>(true))
            {
                light.enabled = false;
            }
            foreach (Animator animator in
                     visualRoot.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = false;
            }
            foreach (MonoBehaviour behaviour in
                     visualRoot.GetComponentsInChildren<
                         MonoBehaviour>(true))
            {
                behaviour.enabled = false;
                Destroy(behaviour);
            }

            float scale = Mathf.Clamp(
                Mathf.Min(
                    7.5f / Mathf.Max(
                        0.01f,
                        initialBounds.size.x),
                    8.4f / Mathf.Max(
                        0.01f,
                        initialBounds.size.z),
                    2.9f / Mathf.Max(
                        0.01f,
                        initialBounds.size.y)),
                0.05f,
                1.25f);
            visualRoot.transform.localScale = Vector3.one * scale;

            if (!TryCalculateRendererBounds(
                    visualRoot,
                    out Bounds scaledBounds))
            {
                error = "模块飞船缩放后无法计算停靠尺寸。";
                return false;
            }

            Vector3 desiredCenter = new Vector3(
                transform.position.x,
                0.12f + scaledBounds.extents.y,
                transform.position.z);
            visualRoot.transform.position +=
                desiredCenter - scaledBounds.center;

            Bounds localBounds =
                CalculateLocalRendererBounds(visualRoot.transform);
            BoxCollider parkedCollider =
                visualRoot.AddComponent<BoxCollider>();
            parkedCollider.center = localBounds.center;
            parkedCollider.size = localBounds.size;
            parkedCollider.isTrigger = false;

            error = string.Empty;
            return true;
        }

        void FailSavedDesign(string message)
        {
            loadingSavedDesign = false;
            usingSavedDesign = false;
            statusMessage = message;
            if (savedVisualRoot != null)
            {
                Destroy(savedVisualRoot);
                savedVisualRoot = null;
            }
            HideDefaultParkedShip();
            Debug.LogError("[SpaceStation] " + message, this);
        }

        void HideDefaultParkedShip()
        {
            if (defaultParkedShip != null &&
                defaultParkedShip.activeSelf)
            {
                defaultParkedShip.SetActive(false);
            }
        }

        bool IsWalkerInInteractionRange()
        {
            if (stationWalker == null)
            {
                return false;
            }

            Vector3 offset =
                stationWalker.position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <=
                   interactionDistance * interactionDistance;
        }

        void OnGUI()
        {
            if (transitionStarted ||
                !IsWalkerInInteractionRange())
            {
                return;
            }

            EnsureGuiStyles();
            float width = Mathf.Min(460f, Screen.width - 40f);
            float left = (Screen.width - width) * 0.5f;
            float top = Screen.height - 116f;
            GUI.Box(
                new Rect(left, top, width, 76f),
                GUIContent.none);
            GUI.Label(
                new Rect(left + 16f, top + 10f, width - 32f, 30f),
                "按 F 进入模块改装",
                promptStyle);
            string detail = loadingSavedDesign
                ? "正在同步已保存的停靠飞船……"
                : usingSavedDesign
                    ? "当前展示：最后一次成功保存的完整设计"
                    : "当前展示：停靠位为空；保存设计后将自动显示";
            GUI.Label(
                new Rect(left + 16f, top + 41f, width - 32f, 22f),
                detail,
                statusStyle);
        }

        void EnsureGuiStyles()
        {
            if (promptStyle != null)
            {
                return;
            }

            promptStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20,
                fontStyle = FontStyle.Bold
            };
            promptStyle.normal.textColor =
                new Color(0.2f, 1f, 0.92f);
            statusStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13
            };
            statusStyle.normal.textColor =
                new Color(0.78f, 0.9f, 0.94f);
        }

        static bool TryCalculateRendererBounds(
            GameObject root,
            out Bounds bounds)
        {
            bounds = default;
            bool initialized = false;
            foreach (Renderer renderer in
                     root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled ||
                    renderer is ParticleSystemRenderer ||
                    renderer is TrailRenderer ||
                    renderer is LineRenderer)
                {
                    continue;
                }

                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return initialized;
        }

        static Bounds CalculateLocalRendererBounds(
            Transform root)
        {
            Bounds localBounds = new Bounds(
                Vector3.zero,
                Vector3.zero);
            bool initialized = false;
            foreach (Renderer renderer in
                     root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled ||
                    renderer is ParticleSystemRenderer ||
                    renderer is TrailRenderer ||
                    renderer is LineRenderer)
                {
                    continue;
                }

                Bounds worldBounds = renderer.bounds;
                Vector3 min = worldBounds.min;
                Vector3 max = worldBounds.max;
                for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    Vector3 corner = root.InverseTransformPoint(
                        new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z));
                    if (!initialized)
                    {
                        localBounds = new Bounds(
                            corner,
                            Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(corner);
                    }
                }
            }
            return initialized
                ? localBounds
                : new Bounds(Vector3.zero, Vector3.one);
        }
    }
}
