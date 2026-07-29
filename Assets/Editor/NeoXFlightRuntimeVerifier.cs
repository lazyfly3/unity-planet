#if UNITY_EDITOR
using System;
using System.Linq;
using ModularAssembly;
using UnityEditor;
using UnityEngine;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.ModularAssembly.Editor
{
    [InitializeOnLoad]
    public static class NeoXFlightRuntimeVerifier
    {
        private const string RunningKey = "NeoXFlightRuntimeVerifier.Running";
        private static int phase;
        private static float deadline;
        private static float settleUntil;
        private static string runtimeId;
        private static string successMessage;
        private static GridAssemblyPresenter presenter;

        static NeoXFlightRuntimeVerifier()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (SessionState.GetBool(RunningKey, false) && EditorApplication.isPlaying)
            {
                EditorApplication.delayCall += Begin;
            }
        }

        [MenuItem("Tools/Modular Assembly/Verify NeoX Flight Runtime")]
        public static void Run()
        {
            phase = 0;
            runtimeId = null;
            successMessage = null;
            presenter = null;
            SessionState.SetBool(RunningKey, true);
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (EditorApplication.isPlaying)
            {
                Begin();
            }
            else
            {
                EditorApplication.isPlaying = true;
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode &&
                SessionState.GetBool(RunningKey, false))
            {
                Begin();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update -= Tick;
                EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            }
        }

        private static void Begin()
        {
            deadline = (float)EditorApplication.timeSinceStartup + 30f;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            try
            {
                if ((float)EditorApplication.timeSinceStartup > deadline)
                {
                    ModularContentService statusService =
                        UnityEngine.Object.FindObjectOfType<ModularContentService>();
                    ModularAssemblyLabController statusController =
                        UnityEngine.Object.FindObjectOfType<ModularAssemblyLabController>();
                    Fail(
                        "Timed out in phase " + phase +
                        "; service=" + (statusService != null) +
                        "; ready=" + (statusService != null && statusService.IsReady) +
                        "; serviceError=" + (statusService != null ? statusService.LastError : "none") +
                        "; integration=" +
                        (UnityEngine.Object.FindObjectOfType<NeoXCatalogIntegration>() != null) +
                        "; controller=" + (statusController != null) +
                        "; model=" + (statusController != null && statusController.Model != null) +
                        "; playerPresenter=" +
                        UnityEngine.Object.FindObjectsOfType<GridAssemblyPresenter>()
                            .Any(item => item.name == "GridShip"));
                    return;
                }
                if (phase == 0)
                {
                    ModularContentService service = UnityEngine.Object.FindObjectOfType<ModularContentService>();
                    NeoXCatalogIntegration integration = UnityEngine.Object.FindObjectOfType<NeoXCatalogIntegration>();
                    ModularAssemblyLabController controller =
                        UnityEngine.Object.FindObjectOfType<ModularAssemblyLabController>();
                    presenter = UnityEngine.Object.FindObjectsOfType<GridAssemblyPresenter>()
                        .FirstOrDefault(item => item.name == "GridShip");
                    if (service == null || !service.IsReady || integration == null ||
                        controller == null || controller.Model == null || presenter == null)
                    {
                        return;
                    }

                    ModularContentRecord record = service.Catalog.Items.First(item =>
                        item.IsModule && item.IsBase &&
                        item.BehaviorKind == GridModuleBehaviorKind.Thruster);
                    integration.Select(record);
                    string moduleId = "neox@" + record.sourceId.Replace("@", "_");
                    bool placed = false;
                    string error = null;
                    for (int x = -5; x <= 5 && !placed; x++)
                    {
                        for (int y = -5; y <= 5 && !placed; y++)
                        {
                            for (int z = -5; z <= 5 && !placed; z++)
                            {
                                GridModulePose pose = new GridModulePose
                                    { x = x, y = y, z = z, orientation = 0 };
                                placed = controller.Model.TryPlace(
                                    moduleId, pose, false, out runtimeId, out error);
                            }
                        }
                    }
                    if (!placed)
                    {
                        Fail("Placement failed: " + error);
                        return;
                    }
                    presenter.Rebuild();
                    phase = 1;
                    deadline = (float)EditorApplication.timeSinceStartup + 60f;
                    return;
                }
                if (phase == 2)
                {
                    if (ModularContentService.HasGlobalPendingAssetRequests ||
                        (float)EditorApplication.timeSinceStartup < settleUntil)
                    {
                        return;
                    }
                    Debug.Log(successMessage);
                    Finish();
                    return;
                }

                GridModuleView view = presenter != null ? presenter.Find(runtimeId) : null;
                MeshFilter loadedMesh = view != null
                    ? view.GetComponentsInChildren<MeshFilter>(true)
                        .FirstOrDefault(filter =>
                            filter.sharedMesh != null &&
                            filter.sharedMesh.vertexCount > 8 &&
                            filter.GetComponent<Renderer>() != null &&
                            filter.GetComponent<Renderer>().enabled)
                    : null;
                Renderer loadedRenderer = loadedMesh != null
                    ? loadedMesh.GetComponent<Renderer>()
                    : null;
                Material loadedMaterial = loadedRenderer != null
                    ? loadedRenderer.sharedMaterial
                    : null;
                bool isUrp = loadedMaterial != null &&
                             loadedMaterial.shader != null &&
                             loadedMaterial.shader.name.Contains("Universal Render Pipeline");
                bool isStandard = loadedMaterial != null &&
                                  loadedMaterial.shader != null &&
                                  loadedMaterial.shader.name == "Standard";
                bool textureReady =
                    (isUrp && loadedMaterial.HasProperty("_BaseMap") &&
                     loadedMaterial.GetTexture("_BaseMap") != null) ||
                    (isStandard && loadedMaterial.HasProperty("_MainTex") &&
                     loadedMaterial.GetTexture("_MainTex") != null);
                bool pbrReady = (isUrp || isStandard) && textureReady;
                if (view == null || view.GetComponent<NeoXBehaviorModule>() == null ||
                    view.transform.childCount <= 1 || loadedMesh == null || !pbrReady)
                {
                    return;
                }
                NeoXBehaviorModule behavior = view.GetComponent<NeoXBehaviorModule>();
                if (behavior.BehaviorKind != GridModuleBehaviorKind.Thruster)
                {
                    Fail("Wrong behavior: " + behavior.BehaviorKind);
                    return;
                }
                if (ModularContentService.HasGlobalPendingAssetRequests ||
                    UnityEngine.Object.FindObjectsOfType<ModularContentService>()
                        .Any(service => service.IsLoadingAssets))
                {
                    return;
                }
                successMessage =
                    $"NeoX flight runtime verification passed: {behavior.SourceId}, " +
                    $"children={view.transform.childCount}, vertices={loadedMesh.sharedMesh.vertexCount}, " +
                    $"shader={loadedMaterial.shader.name}";
                foreach (ModularContentService service in
                         UnityEngine.Object.FindObjectsOfType<ModularContentService>())
                {
                    service.StopAllCoroutines();
                    service.enabled = false;
                }
                foreach (AirBuildExperienceController controller in
                         UnityEngine.Object.FindObjectsOfType<AirBuildExperienceController>())
                {
                    controller.StopAllCoroutines();
                    controller.enabled = false;
                }
                phase = 2;
                settleUntil = (float)EditorApplication.timeSinceStartup + 0.25f;
                deadline = (float)EditorApplication.timeSinceStartup + 30f;
            }
            catch (Exception exception)
            {
                Fail(exception.ToString());
            }
        }

        private static void Fail(string message)
        {
            Debug.LogError("NeoX flight runtime verification failed: " + message);
            Finish();
        }

        private static void Finish()
        {
            SessionState.SetBool(RunningKey, false);
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
