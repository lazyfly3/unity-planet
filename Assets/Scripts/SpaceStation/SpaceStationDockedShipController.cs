using System;
using System.Collections;
using System.Collections.Generic;
using ModularAssembly;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.SpaceStation
{
    sealed class StagedRendererVisibility : MonoBehaviour
    {
        readonly List<Renderer> renderers =
            new List<Renderer>(256);
        bool hidden;

        public void BeginHidden()
        {
            hidden = true;
            CaptureAndHide();
        }

        public void Restore()
        {
            hidden = false;
            CollectRenderers();
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                    renderer.forceRenderingOff = false;
            }
        }

        void LateUpdate()
        {
            if (hidden)
            {
                CaptureAndHide();
            }
        }

        void CaptureAndHide()
        {
            CollectRenderers();
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }
                // Preserve Renderer.enabled. NeoXCatalogIntegration uses that
                // value to permanently retire fallback module geometry once
                // the authored visual is ready. Restoring enabled here would
                // bring those fallback cubes back in the station display.
                // Do not preserve forceRenderingOff for renderers created while
                // NeoX is loading. During that interval the presenter's own
                // presentation blocker also sets it to true; treating that
                // temporary value as an authored state is what previously left
                // every non-core module invisible in the docking bay.
                renderer.forceRenderingOff = true;
            }
        }

        void CollectRenderers()
        {
            renderers.Clear();
            GetComponentsInChildren(true, renderers);
        }
    }

    [DisallowMultipleComponent]
    public sealed class SpaceStationDockedShipController : MonoBehaviour
    {
        sealed class ShadowMeshGpuReadback : IDisposable
        {
            public Mesh Source;
            public int VertexStride;
            public int PositionOffset;
            public int NormalOffset;
            public VertexAttributeFormat NormalFormat;
            public byte[] VertexBytes;
            public byte[] IndexBytes;

            GraphicsBuffer vertexBuffer;
            GraphicsBuffer indexBuffer;
            AsyncGPUReadbackRequest vertexRequest;
            AsyncGPUReadbackRequest indexRequest;

            public bool TryBegin()
            {
                if (Source == null)
                    return false;

                PositionOffset = -1;
                NormalOffset = -1;
                int positionStream = -1;
                int normalStream = -1;
                foreach (VertexAttributeDescriptor attribute in
                         Source.GetVertexAttributes())
                {
                    if (attribute.attribute == VertexAttribute.Position)
                    {
                        if (attribute.format !=
                                VertexAttributeFormat.Float32 ||
                            attribute.dimension < 3)
                        {
                            return false;
                        }
                        positionStream = attribute.stream;
                        PositionOffset = Source.GetVertexAttributeOffset(
                            attribute.attribute);
                    }
                    else if (attribute.attribute == VertexAttribute.Normal)
                    {
                        if ((attribute.format !=
                                 VertexAttributeFormat.Float16 &&
                             attribute.format !=
                                 VertexAttributeFormat.Float32) ||
                            attribute.dimension < 3)
                        {
                            return false;
                        }
                        normalStream = attribute.stream;
                        NormalOffset = Source.GetVertexAttributeOffset(
                            attribute.attribute);
                        NormalFormat = attribute.format;
                    }
                }

                if (PositionOffset < 0 || NormalOffset < 0 ||
                    positionStream != normalStream ||
                    positionStream != 0)
                {
                    return false;
                }

                VertexStride = Source.GetVertexBufferStride(
                    positionStream);
                vertexBuffer = Source.GetVertexBuffer(positionStream);
                indexBuffer = Source.GetIndexBuffer();
                vertexRequest = AsyncGPUReadback.Request(vertexBuffer);
                indexRequest = AsyncGPUReadback.Request(indexBuffer);
                return true;
            }

            public bool TryComplete()
            {
                if (vertexRequest.hasError || indexRequest.hasError)
                    return false;
                VertexBytes = vertexRequest.GetData<byte>().ToArray();
                IndexBytes = indexRequest.GetData<byte>().ToArray();
                return VertexBytes.Length > 0 && IndexBytes.Length > 0;
            }

            public void Dispose()
            {
                vertexBuffer?.Dispose();
                indexBuffer?.Dispose();
                vertexBuffer = null;
                indexBuffer = null;
            }
        }

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
        Mesh combinedShadowMesh;
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
            StagedRendererVisibility stagedVisibility =
                savedVisualRoot.AddComponent<StagedRendererVisibility>();
            stagedVisibility.BeginHidden();

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
            stagedVisibility.Restore();
            if (!TryValidateVisibleModules(
                    presenter,
                    out string visibilityError))
            {
                stagedVisibility.BeginHidden();
                FailSavedDesign(visibilityError);
                yield break;
            }
            if (!TryPrepareStaticVisual(
                    savedVisualRoot,
                    out string visualError))
            {
                stagedVisibility.BeginHidden();
                FailSavedDesign(visualError);
                yield break;
            }

            HideDefaultParkedShip();

            usingSavedDesign = true;
            loadingSavedDesign = false;
            statusMessage = "停靠位已同步最后一次成功保存的模块飞船。";
        }

        static bool TryValidateVisibleModules(
            GridAssemblyPresenter presenter,
            out string error)
        {
            if (presenter == null || presenter.Views.Count == 0)
            {
                error = "停靠飞船没有可验证的模块视图。";
                return false;
            }

            int visibleModules = 0;
            foreach (GridModuleView view in presenter.Views.Values)
            {
                if (view == null)
                    continue;

                bool hasVisibleRenderer = false;
                foreach (Renderer renderer in
                         view.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null || !renderer.enabled ||
                        renderer.forceRenderingOff ||
                        renderer is ParticleSystemRenderer ||
                        renderer is TrailRenderer ||
                        renderer is LineRenderer)
                    {
                        continue;
                    }

                    hasVisibleRenderer = true;
                    break;
                }

                if (hasVisibleRenderer)
                    visibleModules++;
            }

            if (visibleModules == presenter.Views.Count)
            {
                error = string.Empty;
                return true;
            }

            error = "停靠飞船外观不完整：仅 " + visibleModules + "/" +
                    presenter.Views.Count + " 个模块具有可见网格。";
            return false;
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

            DeduplicateOpaqueMaterials(visualRoot);
            TryBuildCombinedOpaqueShadowCaster(visualRoot);

            error = string.Empty;
            return true;
        }

        static void DeduplicateOpaqueMaterials(GameObject visualRoot)
        {
            var canonicalMaterials = new Dictionary<long, Material>();
            var instancedMaterials = new List<Material>();
            foreach (MeshRenderer renderer in
                     visualRoot.GetComponentsInChildren<
                         MeshRenderer>(true))
            {
                if (!renderer.enabled || renderer.forceRenderingOff)
                    continue;
                Material[] materials = renderer.sharedMaterials;
                if (materials.Length != 1 ||
                    !IsOpaqueStandardMaterial(materials[0]))
                {
                    continue;
                }

                Material material = materials[0];
                long key = ((long)material.shader.GetInstanceID() << 32) |
                           (uint)material.ComputeCRC();
                if (canonicalMaterials.TryGetValue(
                        key,
                        out Material canonical))
                {
                    if (canonical != material)
                        renderer.sharedMaterial = canonical;
                }
                else
                {
                    canonicalMaterials.Add(key, material);
                    instancedMaterials.Add(material);
                }
            }

            foreach (Material material in instancedMaterials)
                material.enableInstancing = true;
        }

        void TryBuildCombinedOpaqueShadowCaster(GameObject visualRoot)
        {
            if (visualRoot == null || combinedShadowMesh != null ||
                !SystemInfo.supportsAsyncGPUReadback)
            {
                return;
            }

            var sourceRenderers = new List<MeshRenderer>(128);
            Material shadowMaterial = null;
            int shadowLayer = -1;
            foreach (MeshRenderer renderer in
                     visualRoot.GetComponentsInChildren<
                         MeshRenderer>(true))
            {
                if (!renderer.enabled || renderer.forceRenderingOff ||
                    renderer.shadowCastingMode == ShadowCastingMode.Off)
                {
                    continue;
                }

                if (!TryGetOpaqueShadowSource(
                        renderer,
                        out Mesh sourceMesh,
                        out Material sourceMaterial))
                {
                    return;
                }

                if (shadowLayer < 0)
                {
                    shadowLayer = renderer.gameObject.layer;
                    shadowMaterial = sourceMaterial;
                }
                else if (renderer.gameObject.layer != shadowLayer)
                {
                    return;
                }

                sourceRenderers.Add(renderer);
            }

            if (sourceRenderers.Count < 2 || shadowMaterial == null)
                return;

            var readbacks =
                new Dictionary<Mesh, ShadowMeshGpuReadback>();
            try
            {
                foreach (MeshRenderer renderer in sourceRenderers)
                {
                    Mesh source = renderer
                        .GetComponent<MeshFilter>().sharedMesh;
                    if (readbacks.ContainsKey(source))
                        continue;
                    var readback = new ShadowMeshGpuReadback
                    {
                        Source = source
                    };
                    readbacks.Add(source, readback);
                    if (!readback.TryBegin())
                        return;
                }

                AsyncGPUReadback.WaitAllRequests();
                foreach (ShadowMeshGpuReadback readback in
                         readbacks.Values)
                {
                    if (!readback.TryComplete())
                        return;
                }

                if (!TryCreateCombinedShadowMesh(
                        visualRoot.transform,
                        sourceRenderers,
                        readbacks,
                        out Mesh shadowMesh))
                {
                    return;
                }

                GameObject shadowObject = new GameObject(
                    "ParkedShipCombinedShadowCaster");
                shadowObject.layer = shadowLayer;
                shadowObject.transform.SetParent(
                    visualRoot.transform,
                    false);
                shadowObject.AddComponent<MeshFilter>().sharedMesh =
                    shadowMesh;
                MeshRenderer shadowRenderer =
                    shadowObject.AddComponent<MeshRenderer>();
                shadowRenderer.sharedMaterial = shadowMaterial;
                shadowRenderer.shadowCastingMode =
                    ShadowCastingMode.ShadowsOnly;
                shadowRenderer.receiveShadows = false;
                shadowRenderer.lightProbeUsage = LightProbeUsage.Off;
                shadowRenderer.reflectionProbeUsage =
                    ReflectionProbeUsage.Off;
                shadowRenderer.motionVectorGenerationMode =
                    MotionVectorGenerationMode.ForceNoMotion;

                combinedShadowMesh = shadowMesh;
                foreach (MeshRenderer source in sourceRenderers)
                    source.shadowCastingMode = ShadowCastingMode.Off;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[SpaceStation] Combined parked-ship shadow " +
                    "caster was skipped: " + exception.Message,
                    this);
            }
            finally
            {
                foreach (ShadowMeshGpuReadback readback in
                         readbacks.Values)
                {
                    readback.Dispose();
                }
            }
        }

        static bool TryGetOpaqueShadowSource(
            MeshRenderer renderer,
            out Mesh mesh,
            out Material material)
        {
            mesh = null;
            material = null;
            if (renderer == null ||
                renderer.shadowCastingMode != ShadowCastingMode.On)
            {
                return false;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null ||
                filter.sharedMesh.subMeshCount != 1 ||
                filter.sharedMesh.GetTopology(0) !=
                    MeshTopology.Triangles)
            {
                return false;
            }

            Material[] materials = renderer.sharedMaterials;
            if (materials.Length != 1 ||
                !IsOpaqueStandardMaterial(materials[0]))
            {
                return false;
            }

            mesh = filter.sharedMesh;
            material = materials[0];
            return true;
        }

        static bool IsOpaqueStandardMaterial(Material material)
        {
            return material != null && material.shader != null &&
                   string.Equals(
                       material.shader.name,
                       "Standard",
                       StringComparison.Ordinal) &&
                   material.renderQueue == (int)RenderQueue.Geometry &&
                   !material.IsKeywordEnabled("_ALPHATEST_ON") &&
                   !material.IsKeywordEnabled("_ALPHABLEND_ON") &&
                   !material.IsKeywordEnabled(
                       "_ALPHAPREMULTIPLY_ON") &&
                   (!material.HasProperty("_Mode") ||
                    Mathf.Approximately(
                        material.GetFloat("_Mode"),
                        0f));
        }

        static bool TryCreateCombinedShadowMesh(
            Transform root,
            IReadOnlyList<MeshRenderer> sourceRenderers,
            IReadOnlyDictionary<Mesh, ShadowMeshGpuReadback> readbacks,
            out Mesh shadowMesh)
        {
            shadowMesh = null;
            if (root == null || sourceRenderers == null ||
                sourceRenderers.Count == 0)
            {
                return false;
            }

            int vertexCapacity = 0;
            int indexCapacity = 0;
            foreach (MeshRenderer renderer in sourceRenderers)
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                vertexCapacity += mesh.vertexCount;
                indexCapacity += (int)mesh.GetSubMesh(0).indexCount;
            }

            var vertices = new List<Vector3>(vertexCapacity);
            var normals = new List<Vector3>(vertexCapacity);
            var indices = new List<int>(indexCapacity);
            Matrix4x4 worldToRoot = root.worldToLocalMatrix;

            foreach (MeshRenderer renderer in sourceRenderers)
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (!readbacks.TryGetValue(
                        mesh,
                        out ShadowMeshGpuReadback readback))
                {
                    return false;
                }

                int vertexStart = vertices.Count;
                Matrix4x4 relative =
                    worldToRoot * renderer.transform.localToWorldMatrix;
                Matrix4x4 normalMatrix = relative.inverse.transpose;
                for (int vertex = 0;
                     vertex < mesh.vertexCount;
                     vertex++)
                {
                    int offset = vertex * readback.VertexStride;
                    Vector3 position = ReadFloatVector3(
                        readback.VertexBytes,
                        offset + readback.PositionOffset);
                    Vector3 normal = readback.NormalFormat ==
                                     VertexAttributeFormat.Float16
                        ? ReadHalfVector3(
                            readback.VertexBytes,
                            offset + readback.NormalOffset)
                        : ReadFloatVector3(
                            readback.VertexBytes,
                            offset + readback.NormalOffset);
                    vertices.Add(relative.MultiplyPoint3x4(position));
                    normals.Add(
                        normalMatrix.MultiplyVector(normal).normalized);
                }

                SubMeshDescriptor subMesh = mesh.GetSubMesh(0);
                int indexStride = mesh.indexFormat == IndexFormat.UInt16
                    ? 2
                    : 4;
                bool flipWinding = relative.determinant < 0f;
                for (int index = 0;
                     index < subMesh.indexCount;
                     index += 3)
                {
                    int first = ReadIndex(
                        readback.IndexBytes,
                        (subMesh.indexStart + index) * indexStride,
                        indexStride) + subMesh.baseVertex + vertexStart;
                    int second = ReadIndex(
                        readback.IndexBytes,
                        (subMesh.indexStart + index + 1) * indexStride,
                        indexStride) + subMesh.baseVertex + vertexStart;
                    int third = ReadIndex(
                        readback.IndexBytes,
                        (subMesh.indexStart + index + 2) * indexStride,
                        indexStride) + subMesh.baseVertex + vertexStart;
                    indices.Add(first);
                    indices.Add(flipWinding ? third : second);
                    indices.Add(flipWinding ? second : third);
                }
            }

            if (vertices.Count == 0 || indices.Count == 0)
                return false;

            shadowMesh = new Mesh
            {
                name = "ParkedShipCombinedShadowMesh",
                hideFlags = HideFlags.DontSave,
                indexFormat = vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            shadowMesh.SetVertices(vertices);
            shadowMesh.SetNormals(normals);
            shadowMesh.SetTriangles(indices, 0, true);
            shadowMesh.UploadMeshData(true);
            return true;
        }

        static Vector3 ReadFloatVector3(byte[] bytes, int offset)
        {
            return new Vector3(
                BitConverter.ToSingle(bytes, offset),
                BitConverter.ToSingle(bytes, offset + 4),
                BitConverter.ToSingle(bytes, offset + 8));
        }

        static Vector3 ReadHalfVector3(byte[] bytes, int offset)
        {
            return new Vector3(
                HalfToFloat(BitConverter.ToUInt16(bytes, offset)),
                HalfToFloat(BitConverter.ToUInt16(bytes, offset + 2)),
                HalfToFloat(BitConverter.ToUInt16(bytes, offset + 4)));
        }

        static int ReadIndex(byte[] bytes, int offset, int stride)
        {
            return stride == 2
                ? BitConverter.ToUInt16(bytes, offset)
                : (int)BitConverter.ToUInt32(bytes, offset);
        }

        static float HalfToFloat(ushort value)
        {
            uint sign = (uint)(value & 0x8000) << 16;
            uint exponent = (uint)(value >> 10) & 0x1f;
            uint mantissa = (uint)value & 0x3ff;
            uint bits;
            if (exponent == 0)
            {
                if (mantissa == 0)
                {
                    bits = sign;
                }
                else
                {
                    exponent = 113;
                    while ((mantissa & 0x400) == 0)
                    {
                        mantissa <<= 1;
                        exponent--;
                    }
                    mantissa &= 0x3ff;
                    bits = sign | (exponent << 23) |
                           (mantissa << 13);
                }
            }
            else if (exponent == 31)
            {
                bits = sign | 0x7f800000u | (mantissa << 13);
            }
            else
            {
                bits = sign | ((exponent + 112) << 23) |
                       (mantissa << 13);
            }
            return BitConverter.Int32BitsToSingle((int)bits);
        }

        void OnDestroy()
        {
            if (combinedShadowMesh != null)
            {
                Destroy(combinedShadowMesh);
                combinedShadowMesh = null;
            }
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
