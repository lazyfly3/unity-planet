using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpacecraftEditor.Tests
{
    public sealed class SpacecraftEditorPlayModeTests
    {
        private const string WorkshopRootResource = "Spacecraft/SpacecraftWorkshopRoot";

        private static IEnumerator LoadWorkshopScene()
        {
            var existing = GameObject.Find("GameRoot");
            if (existing != null)
                Object.Destroy(existing);
            yield return null;

            var prefab = Resources.Load<GameObject>(WorkshopRootResource);
            Assert.That(prefab, Is.Not.Null);
            var root = Object.Instantiate(prefab);
            root.name = "GameRoot";
            var persistence = root.GetComponentInChildren<SpacecraftPersistenceCoordinator>(true);
            if (persistence != null)
                persistence.enabled = false;
            yield return null;
        }

        private static Transform WorkshopCanvas(GameObject gameRoot)
        {
            return gameRoot.transform.Find("WorkshopCanvas");
        }

        [UnityTest]
        public IEnumerator RuntimeScene_BuildsUiAndPreservesPlacedPartAcrossFlightMode()
        {
            yield return LoadWorkshopScene();

            var gameRoot = GameObject.Find("GameRoot");
            Assert.That(gameRoot, Is.Not.Null);
            var shipRoot = gameRoot.transform.Find("ShipRoot");
            Assert.That(shipRoot, Is.Not.Null);
            var assembly = shipRoot.GetComponent<ShipAssembly>();
            var catalog = gameRoot.GetComponentInChildren<PartCatalog>();
            var app = gameRoot.GetComponent<SpacecraftApp>();
            Assert.That(assembly, Is.Not.Null);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(app, Is.Not.Null);
            Assert.That(app.IsHullSelectionConfirmed, Is.False);
            Assert.That(WorkshopCanvas(gameRoot).Find("HullSelectionPanel").gameObject.activeSelf, Is.True);
            Assert.That(WorkshopCanvas(gameRoot).Find("BuildInterface").gameObject.activeSelf, Is.False);
            app.EnterFlight();
            Assert.That(app.IsFlightMode, Is.False);
            Assert.That(app.ConfirmHullSelection(), Is.True);
            Assert.That(app.IsHullSelectionConfirmed, Is.True);
            var hullController = gameRoot.GetComponentInChildren<ShipHullController>();
            Assert.That(hullController.PlacementCollider.enabled, Is.True);
            Assert.That(WorkshopCanvas(gameRoot).Find("BuildInterface"), Is.Not.Null);
            Assert.That(WorkshopCanvas(gameRoot).Find("BuildInterface/PartLibraryPanel/PartInspectorPanel/BindThrusterKey"), Is.Not.Null);

            var definition = catalog.Find("thruster.small");
            var part = assembly.AddPart(definition, new Vector3(0f, 0f, -3f), Quaternion.LookRotation(Vector3.back, Vector3.up), 1f);
            Assert.That(part.transform.parent, Is.EqualTo(assembly.PartsRoot));
            Assert.That(assembly.Parts.Count, Is.EqualTo(1));
            Assert.That(Vector3.Dot(part.ExhaustDirection, part.ThrustDirection), Is.EqualTo(-1f).Within(0.0001f));

            part.ApplyThrust(assembly.ShipBody, 1f);
            Assert.That(part.CurrentThrottle, Is.EqualTo(1f));
            Assert.That(part.GetComponentInChildren<ParticleSystem>(true).emission.rateOverTime.constant, Is.GreaterThan(0f));

            app.EnterFlight();
            Assert.That(hullController.PlacementCollider.enabled, Is.False);
            yield return new WaitForFixedUpdate();
            app.ExitFlight();
            yield return null;

            Assert.That(assembly.Parts.Count, Is.EqualTo(1));
            Assert.That(part.transform.parent.name, Is.EqualTo("Parts"));
            Assert.That(app.IsFlightMode, Is.False);
            Assert.That(hullController.PlacementCollider.enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator NativePaint_RestoresAuthoredSlotsWithoutChangingGlass()
        {
            yield return LoadWorkshopScene();

            var gameRoot = GameObject.Find("GameRoot");
            var app = gameRoot.GetComponent<SpacecraftApp>();
            var hullController = gameRoot.GetComponentInChildren<ShipHullController>();
            Assert.That(app.SelectedHull.HullId, Is.EqualTo("hull.a30_thunderbolt"));
            Assert.That(
                hullController.CurrentMaterialId,
                Is.EqualTo(SpacecraftPaintBinding.NativePaintId));

            SpacecraftPaintBinding binding =
                hullController.GetComponentInChildren<SpacecraftPaintBinding>(true);
            Assert.That(binding, Is.Not.Null);
            var before = binding.Bindings
                .Select(entry => entry.Renderer.sharedMaterials.ToArray())
                .ToArray();

            Assert.That(app.ApplyHullMaterial("paint.warning_red"), Is.True);
            for (int rendererIndex = 0; rendererIndex < binding.Bindings.Length; rendererIndex++)
            {
                var entry = binding.Bindings[rendererIndex];
                var paintable = entry.PaintableSlots.ToHashSet();
                Material[] current = entry.Renderer.sharedMaterials;
                for (int slot = 0; slot < current.Length; slot++)
                {
                    if (paintable.Contains(slot))
                        Assert.That(current[slot], Is.Not.SameAs(before[rendererIndex][slot]));
                    else
                        Assert.That(current[slot], Is.SameAs(before[rendererIndex][slot]));
                }
            }

            Assert.That(app.ApplyHullMaterial(SpacecraftPaintBinding.NativePaintId), Is.True);
            for (int rendererIndex = 0; rendererIndex < binding.Bindings.Length; rendererIndex++)
            {
                Material[] restored = binding.Bindings[rendererIndex].Renderer.sharedMaterials;
                Assert.That(restored, Is.EqualTo(before[rendererIndex]));
            }
        }

        [UnityTest]
        public IEnumerator LegacyHullBlueprint_IsRejectedWithoutOverwritingTheSelection()
        {
            yield return LoadWorkshopScene();

            var gameRoot = GameObject.Find("GameRoot");
            var app = gameRoot.GetComponent<SpacecraftApp>();
            var assembly = gameRoot.GetComponentInChildren<ShipAssembly>();
            var legacy = new SpacecraftBlueprintData
            {
                formatVersion = 2,
                hullId = "hull.spindle",
                hullMaterialId = "paint.warning_red",
                parts = System.Array.Empty<PlacedPartState>()
            };

            Assert.That(app.RestoreBlueprint(legacy), Is.False);
            Assert.That(app.IsHullSelectionConfirmed, Is.False);
            Assert.That(app.SelectedHull.HullId, Is.EqualTo("hull.a30_thunderbolt"));
            Assert.That(assembly.Parts, Is.Empty);
        }

        [UnityTest]
        public IEnumerator RestartBuildButton_ClearsAssemblyAndReturnsToHullSelection()
        {
            yield return LoadWorkshopScene();

            var gameRoot = GameObject.Find("GameRoot");
            var app = gameRoot.GetComponent<SpacecraftApp>();
            var assembly = gameRoot.GetComponentInChildren<ShipAssembly>();
            var catalog = gameRoot.GetComponentInChildren<PartCatalog>();
            var history = gameRoot.GetComponentInChildren<CommandHistory>();
            var ui = gameRoot.GetComponentInChildren<WorkshopUIReferences>();
            var builder = gameRoot.GetComponentInChildren<BuildModeController>();

            Assert.That(app.ConfirmHullSelection(), Is.True);
            assembly.AddPart(
                catalog.Find("thruster.small"),
                new Vector3(0f, 0f, -3f),
                Quaternion.LookRotation(Vector3.back, Vector3.up),
                1f);
            history.Record();
            Assert.That(assembly.Parts.Count, Is.EqualTo(1));
            Assert.That(ui.RedoButton.interactable, Is.True);

            ui.RedoButton.onClick.Invoke();
            yield return null;

            Assert.That(app.IsHullSelectionConfirmed, Is.False);
            Assert.That(builder.IsBuildMode, Is.False);
            Assert.That(assembly.Parts.Count, Is.Zero);
            Assert.That(history.CanUndo, Is.False);
            Assert.That(history.CanRedo, Is.False);
            Assert.That(WorkshopCanvas(gameRoot).Find("HullSelectionPanel").gameObject.activeSelf, Is.True);
            Assert.That(WorkshopCanvas(gameRoot).Find("BuildInterface").gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator RearMountedThruster_AcceleratesTowardShipPositiveZ()
        {
            yield return LoadWorkshopScene();

            var gameRoot = GameObject.Find("GameRoot");
            Assert.That(gameRoot.GetComponent<SpacecraftApp>().ConfirmHullSelection(), Is.True);
            var shipRoot = gameRoot.transform.Find("ShipRoot");
            var assembly = shipRoot.GetComponent<ShipAssembly>();
            var catalog = gameRoot.GetComponentInChildren<PartCatalog>();
            var body = assembly.ShipBody;
            var part = assembly.AddPart(
                catalog.Find("thruster.small"),
                new Vector3(0f, 0f, -3f),
                Quaternion.LookRotation(Vector3.back, Vector3.up),
                1f);

            body.isKinematic = false;
            body.useGravity = false;
            part.ApplyThrust(body, 1f);
            yield return new WaitForFixedUpdate();

            Assert.That(Vector3.Dot(body.velocity, shipRoot.forward), Is.GreaterThan(0f));
            Assert.That(Vector3.Dot(part.ExhaustDirection, shipRoot.forward), Is.LessThan(0f));
            Assert.That(Vector3.Dot(part.ThrustDirection, shipRoot.forward), Is.GreaterThan(0f));

            part.SetExhaust(0f);
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        [UnityTest]
        public IEnumerator RuntimeCamera_CentersEditViewportAndUsesRearUpperFlightView()
        {
            yield return LoadWorkshopScene();
            Canvas.ForceUpdateCanvases();
            yield return null;

            var gameRoot = GameObject.Find("GameRoot");
            var shipRoot = gameRoot.transform.Find("ShipRoot");
            var app = gameRoot.GetComponent<SpacecraftApp>();
            var cameraController = gameRoot.GetComponentInChildren<OrbitCameraController>();
            var ui = gameRoot.GetComponentInChildren<EditorUIController>();
            var camera = cameraController.ControlledCamera;

            Assert.That(ui.BuildViewport, Is.Not.Null);
            Assert.That(ui.HullSelectionViewport, Is.Not.Null);
            Assert.That(ui.IsHullSelectionMode, Is.True);
            Assert.That(camera.rect.xMin, Is.GreaterThan(0.1f));
            Assert.That(camera.rect.yMin, Is.GreaterThan(0.05f));
            Assert.That(camera.rect.xMax, Is.EqualTo(1f).Within(0.01f));
            Assert.That(camera.rect.yMax, Is.LessThan(0.98f));

            var shipScreenPosition = camera.WorldToScreenPoint(shipRoot.position);
            Assert.That(Vector2.Distance(shipScreenPosition, camera.pixelRect.center), Is.LessThan(3f));

            Assert.That(app.ConfirmHullSelection(), Is.True);
            Canvas.ForceUpdateCanvases();
            yield return null;
            Assert.That(ui.IsHullSelectionMode, Is.False);
            Assert.That(camera.rect.xMin, Is.GreaterThan(0.1f));

            app.EnterFlight();
            yield return null;
            var localCameraOffset = shipRoot.InverseTransformPoint(camera.transform.position);
            Assert.That(camera.rect, Is.EqualTo(new Rect(0f, 0f, 1f, 1f)));
            Assert.That(localCameraOffset.y, Is.GreaterThan(0f));
            Assert.That(localCameraOffset.z, Is.LessThan(0f));

            cameraController.ResetFlightView();
            localCameraOffset = shipRoot.InverseTransformPoint(camera.transform.position);
            Assert.That(localCameraOffset.y, Is.GreaterThan(0f));
            Assert.That(localCameraOffset.z, Is.LessThan(0f));

            var worldOffsetBeforeSpin = camera.transform.position - shipRoot.position;
            shipRoot.rotation = Quaternion.Euler(42f, 73f, 118f);
            cameraController.ResetFlightView();
            var worldOffsetAfterSpin = camera.transform.position - shipRoot.position;
            Assert.That(Vector3.Distance(worldOffsetBeforeSpin, worldOffsetAfterSpin), Is.LessThan(0.001f));
            Assert.That(Vector3.Dot(camera.transform.up, Vector3.up), Is.GreaterThan(0.8f));

            app.ExitFlight();
            yield return null;
            Assert.That(camera.rect.xMin, Is.GreaterThan(0.1f));
        }

        [UnityTest]
        public IEnumerator RearCenterPlacement_SnapsExhaustRearwardAndRejectsDuplicate()
        {
            yield return LoadWorkshopScene();
            Canvas.ForceUpdateCanvases();
            yield return null;

            var gameRoot = GameObject.Find("GameRoot");
            Assert.That(gameRoot.GetComponent<SpacecraftApp>().ConfirmHullSelection(), Is.True);
            var shipRoot = gameRoot.transform.Find("ShipRoot");
            var assembly = shipRoot.GetComponent<ShipAssembly>();
            var catalog = gameRoot.GetComponentInChildren<PartCatalog>();
            var builder = gameRoot.GetComponentInChildren<BuildModeController>();
            var camera = gameRoot.GetComponentInChildren<OrbitCameraController>().ControlledCamera;
            camera.transform.SetPositionAndRotation(
                shipRoot.TransformPoint(new Vector3(0f, 0f, -10f)),
                Quaternion.LookRotation(shipRoot.forward, shipRoot.up));
            var rearScreenPoint = camera.pixelRect.center;

            builder.BeginPlacement(catalog.Find("thruster.small"));
            builder.UpdatePlacement(rearScreenPoint);
            var preview = GameObject.Find("PlacementPreview");

            Assert.That(preview, Is.Not.Null);
            Assert.That(builder.ActiveSnapAxis, Is.EqualTo(PlacementSnapAxis.Rear));
            Assert.That(builder.IsCenterSnapped, Is.True);
            Assert.That(builder.IsPlacementPreviewValid, Is.True);
            Assert.That(Vector3.Dot(preview.transform.forward, shipRoot.forward), Is.LessThan(-0.999f));
            Assert.That(shipRoot.InverseTransformPoint(preview.transform.position).z, Is.EqualTo(-3.025f).Within(0.12f));

            var snapText = WorkshopCanvas(gameRoot).Find("BuildInterface/SnapStatus").GetComponent<UnityEngine.UI.Text>();
            Assert.That(snapText.text, Does.Contain("正后方"));
            Assert.That(snapText.text, Does.Contain("推力 +Z"));

            builder.EndPlacement(rearScreenPoint);
            Assert.That(assembly.Parts.Count, Is.EqualTo(1));
            var part = assembly.Parts[0];
            Assert.That(Vector3.Dot(part.ExhaustDirection, shipRoot.forward), Is.LessThan(-0.999f));
            Assert.That(Vector3.Dot(part.ThrustDirection, shipRoot.forward), Is.GreaterThan(0.999f));

            builder.BeginPlacement(catalog.Find("thruster.small"));
            builder.UpdatePlacement(rearScreenPoint);
            Assert.That(builder.IsPlacementPreviewValid, Is.False);
            builder.EndPlacement(rearScreenPoint);
            Assert.That(assembly.Parts.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator HullSelection_PreviewsAllDefinitionsUpdatesPhysicsAndLocksOnConfirmation()
        {
            yield return LoadWorkshopScene();
            Canvas.ForceUpdateCanvases();
            yield return null;

            var gameRoot = GameObject.Find("GameRoot");
            var app = gameRoot.GetComponent<SpacecraftApp>();
            var hullCatalog = gameRoot.GetComponentInChildren<HullCatalog>();
            var hullController = gameRoot.GetComponentInChildren<ShipHullController>();
            var assembly = gameRoot.GetComponentInChildren<ShipAssembly>();
            var builder = gameRoot.GetComponentInChildren<BuildModeController>();
            var cameraController = gameRoot.GetComponentInChildren<OrbitCameraController>();
            var hullRoot = hullController.transform;

            Assert.That(hullCatalog.Definitions.Count, Is.EqualTo(5));
            Assert.That(builder.IsBuildMode, Is.False);
            var distances = new float[hullCatalog.Definitions.Count];
            for (var index = 0; index < hullCatalog.Definitions.Count; index++)
            {
                var definition = hullCatalog.Definitions[index];
                Assert.That(app.PreviewHull(definition), Is.True);
                distances[index] = cameraController.EditDistance;
                Assert.That(hullController.CurrentHull, Is.EqualTo(definition));
                Assert.That(hullController.HullCollider.sharedMesh, Is.EqualTo(definition.CollisionMesh));
                Assert.That(assembly.HullMass, Is.EqualTo(definition.BaseMass).Within(0.001f));
                Assert.That(assembly.Metrics.totalMass, Is.EqualTo(definition.BaseMass).Within(0.001f));
                Assert.That(builder.HullCollider, Is.EqualTo(hullController.PlacementCollider));
                Assert.That(
                    builder.HullLocalHalfExtents,
                    Is.EqualTo(definition.PlacementSurfaceMesh.bounds.extents));

                var axes = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                foreach (var localAxis in axes)
                {
                    var distance = definition.CollisionMesh.bounds.extents.magnitude + 2f;
                    var worldAxis = hullRoot.TransformDirection(localAxis);
                    RaycastHit hit;
                    Assert.That(hullController.HullCollider.Raycast(
                        new Ray(hullRoot.position + worldAxis * distance, -worldAxis), out hit, distance * 2f), Is.True);
                    var localHit = hullRoot.InverseTransformPoint(hit.point);
                    var expectedRadius = Mathf.Abs(localAxis.x) > 0.5f
                        ? definition.Dimensions.x * 0.5f
                        : Mathf.Abs(localAxis.y) > 0.5f
                            ? definition.Dimensions.y * 0.5f
                            : definition.Dimensions.z * 0.5f;
                    float hitRadius = Mathf.Abs(Vector3.Dot(localHit, localAxis));
                    Assert.That(hitRadius, Is.GreaterThan(0.001f));
                    Assert.That(hitRadius, Is.LessThanOrEqualTo(expectedRadius + 0.06f));
                }

                var modelCount = 0;
                for (var childIndex = 0; childIndex < hullRoot.childCount; childIndex++)
                {
                    if (hullRoot.GetChild(childIndex).name == "Model")
                        modelCount++;
                }
                Assert.That(modelCount, Is.EqualTo(1));
            }

            Assert.That(distances.All(distance => distance > 0f), Is.True);
            var lockedHull = hullController.CurrentHull;
            Assert.That(lockedHull.HullId, Is.EqualTo("hull.sf_fighter_gr2"));
            Assert.That(app.ConfirmHullSelection(), Is.True);
            Assert.That(builder.IsBuildMode, Is.True);
            Assert.That(WorkshopCanvas(gameRoot).Find("HullSelectionPanel").gameObject.activeSelf, Is.False);
            Assert.That(WorkshopCanvas(gameRoot).Find("BuildInterface").gameObject.activeSelf, Is.True);
            Assert.That(app.PreviewHull(hullCatalog.DefaultDefinition), Is.False);
            Assert.That(hullController.CurrentHull, Is.EqualTo(lockedHull));
        }

        [UnityTest]
        public IEnumerator MirrorBindings_StaySynchronizedAndUndoAsOneHistoryStep()
        {
            yield return LoadWorkshopScene();

            var gameRoot = GameObject.Find("GameRoot");
            Assert.That(gameRoot.GetComponent<SpacecraftApp>().ConfirmHullSelection(), Is.True);
            var shipRoot = gameRoot.transform.Find("ShipRoot");
            var assembly = shipRoot.GetComponent<ShipAssembly>();
            var catalog = gameRoot.GetComponentInChildren<PartCatalog>();
            var builder = gameRoot.GetComponentInChildren<BuildModeController>();
            var history = gameRoot.GetComponentInChildren<CommandHistory>();
            var definition = catalog.Find("thruster.small");
            var group = "binding-mirror-group";
            var rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            var left = assembly.AddPart(definition, new Vector3(-0.8f, 0f, -2.7f), rotation, 1f, "binding-left", group);
            var right = assembly.AddPart(definition, new Vector3(0.8f, 0f, -2.7f), rotation, 1f, "binding-right", group);
            history.Record();

            Assert.That(builder.SetPartActivationKey(left, KeyCode.W), Is.True);
            Assert.That(left.ActivationKey, Is.EqualTo(KeyCode.W));
            Assert.That(right.ActivationKey, Is.EqualTo(KeyCode.W));

            builder.Undo();
            yield return null;
            Assert.That(assembly.FindByRuntimeId("binding-left").ActivationKey, Is.EqualTo(KeyCode.None));
            Assert.That(assembly.FindByRuntimeId("binding-right").ActivationKey, Is.EqualTo(KeyCode.None));

            builder.Redo();
            yield return null;
            Assert.That(assembly.FindByRuntimeId("binding-left").ActivationKey, Is.EqualTo(KeyCode.W));
            Assert.That(assembly.FindByRuntimeId("binding-right").ActivationKey, Is.EqualTo(KeyCode.W));

            var independent = assembly.AddPart(definition, new Vector3(0f, 0.7f, -2.7f), rotation, 1f, "binding-independent");
            Assert.That(builder.SetPartActivationKey(independent, KeyCode.W), Is.True);
            Assert.That(independent.ActivationKey, Is.EqualTo(KeyCode.W));
        }

        [UnityTest]
        public IEnumerator PerThrusterInput_OnlyFiresMatchingGroupsAndSupportsSimultaneousKeys()
        {
            yield return LoadWorkshopScene();

            var gameRoot = GameObject.Find("GameRoot");
            Assert.That(gameRoot.GetComponent<SpacecraftApp>().ConfirmHullSelection(), Is.True);
            var shipRoot = gameRoot.transform.Find("ShipRoot");
            var assembly = shipRoot.GetComponent<ShipAssembly>();
            var catalog = gameRoot.GetComponentInChildren<PartCatalog>();
            var flight = shipRoot.GetComponent<ShipFlightController>();
            var body = assembly.ShipBody;
            var definition = catalog.Find("thruster.small");
            var rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            var first = assembly.AddPart(definition, new Vector3(-0.7f, 0f, -2.8f), rotation, 1f, "input-first", null, KeyCode.Alpha1);
            var second = assembly.AddPart(definition, new Vector3(0.7f, 0f, -2.8f), rotation, 1f, "input-second", null, KeyCode.Alpha2);
            var unbound = assembly.AddPart(definition, new Vector3(0f, 0.7f, -2.8f), rotation, 1f, "input-unbound");

            body.isKinematic = false;
            body.useGravity = false;
            flight.ApplyThrusterInputs(key => key == KeyCode.Alpha1);
            Assert.That(first.CurrentThrottle, Is.EqualTo(1f));
            Assert.That(second.CurrentThrottle, Is.Zero);
            Assert.That(unbound.CurrentThrottle, Is.Zero);
            Assert.That(flight.Throttle, Is.EqualTo(1f));
            Assert.That(first.GetComponentInChildren<ParticleSystem>(true).emission.rateOverTime.constant, Is.GreaterThan(0f));
            Assert.That(second.GetComponentInChildren<ParticleSystem>(true).emission.rateOverTime.constant, Is.Zero);
            yield return new WaitForFixedUpdate();
            Assert.That(Vector3.Dot(body.velocity, shipRoot.forward), Is.GreaterThan(0f));

            flight.ApplyThrusterInputs(key => key == KeyCode.Alpha1 || key == KeyCode.Alpha2);
            Assert.That(first.CurrentThrottle, Is.EqualTo(1f));
            Assert.That(second.CurrentThrottle, Is.EqualTo(1f));
            Assert.That(unbound.CurrentThrottle, Is.Zero);

            flight.ApplyThrusterInputs(key => false);
            Assert.That(first.CurrentThrottle, Is.Zero);
            Assert.That(second.CurrentThrottle, Is.Zero);
            Assert.That(flight.Throttle, Is.Zero);
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }
    }
}
