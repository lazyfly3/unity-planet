using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpacecraftEditor
{
    [DisallowMultipleComponent]
    public sealed class SpacecraftApp : MonoBehaviour
    {
        [SerializeField] private PartCatalog catalog;
        [SerializeField] private HullCatalog hullCatalog;
        [SerializeField] private ShipHullController hullController;
        [SerializeField] private ShipAssembly assembly;
        [SerializeField] private BuildModeController buildController;
        [SerializeField] private CommandHistory history;
        [SerializeField] private ShipFlightController flightController;
        [SerializeField] private OrbitCameraController cameraController;
        [SerializeField] private EditorUIController uiController;
        [SerializeField] private ForceVisualizer forceVisualizer;
        [SerializeField] private Camera mainCamera;
        [SerializeField] private Collider hullCollider;
        [SerializeField] private Transform centerOfMassMarker;
        [SerializeField] private SpacecraftPersistenceCoordinator persistenceCoordinator;
        [SerializeField] private string interstellarSceneName = "InterstellarFlight";

        private bool hullSelectionConfirmed;
        private bool sceneLoadRequested;

        public event Action StateChanged;

        public bool IsFlightMode => flightController != null && flightController.IsFlying;
        public bool IsHullSelectionConfirmed => hullSelectionConfirmed;
        public ShipHullDefinition SelectedHull => hullController == null ? null : hullController.CurrentHull;
        public string HullMaterialId => hullController == null ? string.Empty : hullController.CurrentMaterialId;

        private void Awake()
        {
            ResolveReferences();
            var body = assembly.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.drag = 0f;
            body.angularDrag = 0f;

            var defaultHull = hullCatalog == null ? null : hullCatalog.DefaultDefinition;
            assembly.Configure(body, assembly.transform.Find("Parts"), catalog,
                defaultHull == null ? 100f : defaultHull.BaseMass);
            history.Configure(assembly, hullController);
            buildController.Configure(assembly, history, mainCamera, hullCollider);
            buildController.SetBuildMode(false);
            flightController.Configure(this, assembly, body);
            forceVisualizer.Configure(assembly, buildController, centerOfMassMarker);
            forceVisualizer.SetVisible(false);
            uiController.Configure(this, catalog, hullCatalog, hullController, assembly, buildController,
                history, flightController, forceVisualizer);
            cameraController.Configure(mainCamera, assembly.transform, uiController.HullSelectionViewport);
            uiController.SetHullSelectionMode(true);
            if (defaultHull != null)
                PreviewHull(defaultHull);
        }

        public bool PreviewHull(ShipHullDefinition definition)
        {
            if (hullSelectionConfirmed || hullController == null || definition == null ||
                !hullController.ApplyHull(definition))
                return false;

            hullCollider = hullController.HullCollider;
            assembly.SetHullMass(definition.BaseMass);
            buildController.SetHullCollider(hullCollider);
            cameraController.FrameHull(hullController.LocalBounds);
            return true;
        }

        public bool ConfirmHullSelection()
        {
            if (hullSelectionConfirmed)
                return true;
            if (SelectedHull == null)
            {
                var fallback = hullCatalog == null ? null : hullCatalog.DefaultDefinition;
                if (!PreviewHull(fallback))
                    return false;
            }

            hullSelectionConfirmed = true;
            uiController.SetHullSelectionMode(false);
            cameraController.SetEditorViewport(uiController.BuildViewport);
            cameraController.FrameHull(hullController.LocalBounds);
            buildController.SetBuildMode(true);
            forceVisualizer.SetVisible(true);
            assembly.Recalculate();
            StateChanged?.Invoke();
            return true;
        }

        public void RestartBuild()
        {
            if (sceneLoadRequested)
                return;

            if (IsFlightMode)
                ExitFlight();

            buildController.SetBuildMode(false);
            forceVisualizer.SetVisible(false);
            hullSelectionConfirmed = false;
            assembly.RestoreStates(Array.Empty<PlacedPartState>());
            history.Configure(assembly, hullController);

            uiController.SetHullSelectionMode(true);
            cameraController.SetFlightMode(false);
            cameraController.SetEditorViewport(uiController.HullSelectionViewport);
            if (hullController != null && hullController.CurrentHull != null)
                cameraController.FrameHull(hullController.LocalBounds);
            StateChanged?.Invoke();
        }

        public bool RestoreBlueprint(SpacecraftBlueprintData blueprint)
        {
            if (blueprint == null || hullSelectionConfirmed || hullCatalog == null)
                return false;
            var hull = hullCatalog.Find(blueprint.hullId);
            if (hull == null || !PreviewHull(hull))
                return false;
            if (!string.IsNullOrEmpty(blueprint.hullMaterialId))
                hullController.ApplyPaint(blueprint.hullMaterialId);
            assembly.RestoreStates(blueprint.parts);
            history.Configure(assembly, hullController);
            return ConfirmHullSelection();
        }

        public SpacecraftBlueprintData CaptureBlueprint()
        {
            if (!hullSelectionConfirmed || SelectedHull == null)
                return null;
            return new SpacecraftBlueprintData
            {
                formatVersion = 2,
                hullId = SelectedHull.HullId,
                hullMaterialId = HullMaterialId,
                parts = assembly.CaptureStates().ToArray(),
                savedUtcTicks = DateTime.UtcNow.Ticks
            };
        }

        public bool ApplyHullMaterial(string materialId)
        {
            if (hullController == null || !hullController.ApplyPaint(materialId))
                return false;
            history?.Record();
            StateChanged?.Invoke();
            return true;
        }

        public void EnterFlight()
        {
            if (!hullSelectionConfirmed || IsFlightMode)
                return;
            buildController.SetBuildMode(false);
            forceVisualizer.SetVisible(false);
            uiController.SetFlightMode(true);
            cameraController.SetFlightMode(true);
            flightController.EnterFlight();
            StateChanged?.Invoke();
        }

        public void EnterSpace()
        {
            if (!hullSelectionConfirmed || IsFlightMode || sceneLoadRequested)
                return;
            if (string.IsNullOrWhiteSpace(interstellarSceneName) ||
                !Application.CanStreamedLevelBeLoaded(interstellarSceneName))
            {
                Debug.LogError($"Spacecraft workshop cannot load scene '{interstellarSceneName}'.", this);
                return;
            }
            if (persistenceCoordinator == null || !persistenceCoordinator.SaveCurrentBlueprint())
            {
                Debug.LogError("Spacecraft workshop could not save the current blueprint before entering space.", this);
                return;
            }

            sceneLoadRequested = true;
            buildController.SetBuildMode(false);
            forceVisualizer.SetVisible(false);
            SceneManager.LoadScene(interstellarSceneName);
        }

        public void ExitFlight()
        {
            if (!IsFlightMode)
                return;
            flightController.ExitFlight();
            cameraController.SetFlightMode(false);
            buildController.SetBuildMode(true);
            forceVisualizer.SetVisible(true);
            uiController.SetFlightMode(false);
            assembly.Recalculate();
            StateChanged?.Invoke();
        }

        private void ResolveReferences()
        {
            if (catalog == null) catalog = GetComponentInChildren<PartCatalog>(true);
            if (hullCatalog == null) hullCatalog = GetComponentInChildren<HullCatalog>(true);
            if (assembly == null) assembly = GetComponentInChildren<ShipAssembly>(true);
            if (buildController == null) buildController = GetComponentInChildren<BuildModeController>(true);
            if (history == null) history = GetComponentInChildren<CommandHistory>(true);
            if (flightController == null) flightController = GetComponentInChildren<ShipFlightController>(true);
            if (cameraController == null) cameraController = GetComponentInChildren<OrbitCameraController>(true);
            if (uiController == null) uiController = GetComponentInChildren<EditorUIController>(true);
            if (forceVisualizer == null) forceVisualizer = GetComponentInChildren<ForceVisualizer>(true);
            if (mainCamera == null) mainCamera = Camera.main;
            if (hullController == null)
            {
                var hull = assembly.transform.Find("Hull");
                if (hull != null) hullController = hull.GetComponent<ShipHullController>();
            }
            if (hullCollider == null)
            {
                var hull = assembly.transform.Find("Hull");
                if (hullController != null) hullCollider = hullController.HullCollider;
                else if (hull != null) hullCollider = hull.GetComponentInChildren<Collider>();
            }
            if (centerOfMassMarker == null) centerOfMassMarker = assembly.transform.Find("CenterOfMassMarker");
            if (persistenceCoordinator == null)
                persistenceCoordinator = GetComponentInChildren<SpacecraftPersistenceCoordinator>(true);
        }
    }
}
