using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpacecraftEditor.Tests
{
    public sealed class SpacecraftEditorEditModeTests
    {
        [TestCase("thruster_small", "thruster.small", 90f, 20000f)]
        [TestCase("thruster_medium", "thruster.medium", 300f, 80000f)]
        [TestCase("thruster_large", "thruster.large", 950f, 300000f)]
        public void PartDefinitions_HaveExpectedValues(string assetName, string expectedId, float expectedMass, float expectedThrust)
        {
            var definition = AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(
                "Assets/SpacecraftEditor/Data/" + assetName + ".asset");
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.PartId, Is.EqualTo(expectedId));
            Assert.That(definition.BaseMass, Is.EqualTo(expectedMass).Within(0.001f));
            Assert.That(definition.BaseThrust, Is.EqualTo(expectedThrust).Within(0.001f));
            Assert.That(definition.Prefab, Is.Not.Null);
            Assert.That(definition.Thumbnail, Is.Not.Null);
        }

        [TestCase("hull_balanced", "hull.balanced", 12000f, 3f, 2.2f, 6f)]
        [TestCase("hull_spindle", "hull.spindle", 9000f, 2.2f, 1.8f, 7.5f)]
        [TestCase("hull_saucer", "hull.saucer", 18000f, 5.2f, 1.4f, 4.6f)]
        public void HullDefinitions_HaveUniquePlayableModelsAndColliderBounds(
            string assetName, string expectedId, float expectedMass, float width, float height, float length)
        {
            var definition = AssetDatabase.LoadAssetAtPath<ShipHullDefinition>(
                "Assets/SpacecraftEditor/Data/" + assetName + ".asset");
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.HullId, Is.EqualTo(expectedId));
            Assert.That(definition.BaseMass, Is.EqualTo(expectedMass).Within(0.001f));
            Assert.That(definition.Dimensions, Is.EqualTo(new Vector3(width, height, length)));
            Assert.That(definition.ModelPrefab, Is.Not.Null);
            Assert.That(definition.Thumbnail, Is.Not.Null);
            Assert.That(definition.CollisionMesh, Is.Not.Null);
            Assert.That(definition.CollisionMesh.bounds.size.x, Is.EqualTo(width).Within(0.01f));
            // Source FBX meshes use X/Z as the horizontal/vertical cross-section and
            // Y as the longitudinal axis. The prefab rotates that authored basis into
            // Unity's gameplay X/Y/Z convention.
            Assert.That(definition.CollisionMesh.bounds.size.y, Is.EqualTo(length).Within(0.01f));
            Assert.That(definition.CollisionMesh.bounds.size.z, Is.EqualTo(height).Within(0.01f));
        }

        [Test]
        public void HullDefinitions_UseUniqueIds()
        {
            var definitions = AssetDatabase.FindAssets("t:ShipHullDefinition", new[] { "Assets/SpacecraftEditor/Data" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<ShipHullDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(definition => definition != null)
                .ToArray();
            Assert.That(definitions.Length, Is.EqualTo(3));
            Assert.That(definitions.Select(definition => definition.HullId).Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void ThrusterScale_UsesSquareThrustAndCubicMass()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(
                "Assets/SpacecraftEditor/Data/thruster_medium.asset");
            var instance = Object.Instantiate(definition.Prefab);
            try
            {
                var thruster = instance.GetComponent<ThrusterPart>();
                thruster.Configure(definition, 1.5f, "test", string.Empty);
                Assert.That(thruster.ActualThrust, Is.EqualTo(180000f).Within(0.001f));
                Assert.That(thruster.ActualMass, Is.EqualTo(1012.5f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void MirrorRotation_IsAnInvolution()
        {
            var original = Quaternion.Euler(23f, -41f, 67f);
            var mirrored = BuildModeController.ReflectLocalRotation(original);
            var restored = BuildModeController.ReflectLocalRotation(mirrored);
            Assert.That(Quaternion.Angle(original, restored), Is.LessThan(0.01f));
        }

        [TestCase(1f, 1f)]
        [TestCase(-1f, -1f)]
        public void LateralWingRotation_OpensOutwardAndKeepsItsLeadingAxisForward(
            float side,
            float expectedOutwardX)
        {
            var rotation = BuildModeController.BuildLateralWingLocalRotation(side, 0f);

            Assert.That(Vector3.Dot(rotation * Vector3.left, Vector3.right * expectedOutwardX),
                Is.GreaterThan(0.999f));
            Assert.That(Vector3.Dot(rotation * Vector3.up, Vector3.forward), Is.GreaterThan(0.999f));
            Assert.That(Mathf.Abs(Vector3.Dot(rotation * Vector3.forward, Vector3.up)), Is.GreaterThan(0.999f));
        }

        [TestCase("decor_swept_wing")]
        [TestCase("decor_delta_wing")]
        [TestCase("decor_canard")]
        [TestCase("decor_vertical_fin")]
        [TestCase("decor_armor_fairing")]
        [TestCase("decor_radiator")]
        [TestCase("decor_sensor_mast")]
        [TestCase("decor_engine_nacelle")]
        public void IndustrialGreebleDefinitions_UseSurfaceConformingPlacement(string assetName)
        {
            var definition = AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(
                "Assets/SpacecraftEditor/Data/ModularParts/" + assetName + ".asset");

            Assert.That(definition, Is.Not.Null);
            Assert.That(
                definition.PlacementMode,
                Is.EqualTo(SpacecraftPartPlacementMode.SurfaceConforming));
        }

        [Test]
        public void ThrusterPrefabs_ContainColliderAndExhaustParticles()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:ShipPartDefinition", new[] { "Assets/SpacecraftEditor/Data" }))
            {
                var definition = AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition == null || definition.Category != SpacecraftPartCategory.Thruster)
                    continue;
                Assert.That(definition.Prefab.GetComponent<BoxCollider>(), Is.Not.Null, definition.PartId);
                var exhaustVfx = definition.Prefab.GetComponentInChildren<ThrusterExhaustVfx>(true);
                Assert.That(exhaustVfx, Is.Not.Null, definition.PartId);

                var particleSystems = exhaustVfx.GetComponentsInChildren<ParticleSystem>(true);
                Assert.That(particleSystems.Length, Is.GreaterThanOrEqualTo(7), definition.PartId);
                Assert.That(definition.Prefab.GetComponentInChildren<ParticleSystemRenderer>(true).sharedMaterial, Is.Not.Null, definition.PartId);

                var importedVfx = exhaustVfx.transform.Find("UniqueThrusterVfx");
                Assert.That(importedVfx, Is.Not.Null, definition.PartId);
                Assert.That(Vector3.Dot(importedVfx.right, definition.Prefab.transform.forward),
                    Is.GreaterThan(0.999f), definition.PartId);

                var demoShip = importedVfx.Find("SpaceshipMesh");
                Assert.That(demoShip, Is.Not.Null, definition.PartId);
                Assert.That(demoShip.gameObject.activeSelf, Is.False, definition.PartId);
            }
        }

        [Test]
        public void ThrusterThrottle_ExtendsPlumeWithoutMovingItsNozzleAnchor()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(
                "Assets/SpacecraftEditor/Data/thruster_medium.asset");
            var instance = Object.Instantiate(definition.Prefab);
            try
            {
                var thruster = instance.GetComponent<ThrusterPart>();
                var plume = instance.transform.Find("ExhaustFX/UniqueThrusterVfx");
                Assert.That(plume, Is.Not.Null);

                thruster.SetExhaust(0.15f);
                float lowThrottleLength = plume.localScale.x;
                Vector3 lowThrottleAnchor = plume.TransformPoint(new Vector3(1.73f, 0f, 0.03f));

                thruster.SetExhaust(1f);
                float fullThrottleLength = plume.localScale.x;
                Vector3 fullThrottleAnchor = plume.TransformPoint(new Vector3(1.73f, 0f, 0.03f));

                Assert.That(fullThrottleLength, Is.GreaterThan(lowThrottleLength * 2f));
                Assert.That(Vector3.Distance(lowThrottleAnchor, fullThrottleAnchor), Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ThrusterDirections_ModelAndParticlesUsePositiveZForExhaust()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:ShipPartDefinition", new[] { "Assets/SpacecraftEditor/Data" }))
            {
                var definition = AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition == null || definition.Category != SpacecraftPartCategory.Thruster)
                    continue;
                var instance = Object.Instantiate(definition.Prefab);
                try
                {
                    var thruster = instance.GetComponent<ThrusterPart>();
                    thruster.Configure(definition, 1f, "direction-test", string.Empty);
                    instance.transform.rotation = Quaternion.Euler(17f, 31f, -12f);

                    Assert.That(Vector3.Dot(thruster.ExhaustDirection, thruster.ThrustDirection), Is.EqualTo(-1f).Within(0.0001f), definition.PartId);
                    Assert.That(Vector3.Dot(thruster.ForceDirection, thruster.ThrustDirection), Is.EqualTo(1f).Within(0.0001f), definition.PartId);

                    var particles = instance.GetComponentInChildren<ParticleSystem>(true);
                    Assert.That(Vector3.Dot(particles.transform.forward, thruster.ExhaustDirection), Is.EqualTo(1f).Within(0.0001f), definition.PartId);
                    Assert.That(instance.transform.InverseTransformPoint(particles.transform.position).z, Is.GreaterThan(0f), definition.PartId);

                    var nozzle = instance.GetComponentsInChildren<Renderer>(true)
                        .FirstOrDefault(renderer => renderer.name.Contains("Nozzle"));
                    Assert.That(nozzle, Is.Not.Null, definition.PartId);
                    Assert.That(instance.transform.InverseTransformPoint(nozzle.bounds.center).z, Is.GreaterThan(0f), definition.PartId);
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void AssemblyMetrics_RearThrustersProduceForwardForceAndExpectedTorque()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(
                "Assets/SpacecraftEditor/Data/thruster_small.asset");
            var ship = new GameObject("DirectionTestShip", typeof(Rigidbody), typeof(ShipAssembly));
            var parts = new GameObject("Parts").transform;
            parts.SetParent(ship.transform, false);
            try
            {
                var assembly = ship.GetComponent<ShipAssembly>();
                assembly.Configure(ship.GetComponent<Rigidbody>(), parts, null, 100f);
                var rearFacing = Quaternion.LookRotation(Vector3.back, Vector3.up);

                assembly.AddPart(definition, new Vector3(1f, 0f, -3f), rearFacing, 1f);
                Assert.That(assembly.Metrics.localResultantForce.z, Is.GreaterThan(0f));
                Assert.That(assembly.Metrics.localResultantTorque.y, Is.LessThan(0f));

                assembly.AddPart(definition, new Vector3(-1f, 0f, -3f), rearFacing, 1f);
                Assert.That(assembly.Metrics.localResultantForce.z, Is.EqualTo(40000f).Within(0.01f));
                Assert.That(assembly.Metrics.localResultantTorque.magnitude, Is.LessThan(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(ship);
            }
        }

        [Test]
        public void AssemblyMetrics_UsesSelectedHullMassForAcceleration()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(
                "Assets/SpacecraftEditor/Data/thruster_small.asset");
            var ship = new GameObject("HullMassTestShip", typeof(Rigidbody), typeof(ShipAssembly));
            var parts = new GameObject("Parts").transform;
            parts.SetParent(ship.transform, false);
            try
            {
                var assembly = ship.GetComponent<ShipAssembly>();
                assembly.Configure(ship.GetComponent<Rigidbody>(), parts, null, 100f);
                assembly.AddPart(definition, new Vector3(0f, 0f, -3f),
                    Quaternion.LookRotation(Vector3.back, Vector3.up), 1f);

                assembly.SetHullMass(9000f);
                var lightAcceleration = assembly.Metrics.Acceleration;
                Assert.That(assembly.Metrics.totalMass, Is.EqualTo(9090f).Within(0.001f));

                assembly.SetHullMass(18000f);
                var heavyAcceleration = assembly.Metrics.Acceleration;
                Assert.That(assembly.Metrics.totalMass, Is.EqualTo(18090f).Within(0.001f));
                Assert.That(lightAcceleration, Is.GreaterThan(heavyAcceleration));
                Assert.That(heavyAcceleration, Is.EqualTo(20000f / 18090f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(ship);
            }
        }

        [Test]
        public void PlacementSnap_ClassifiesAllSixAxesAndLeavesDiagonalGapFree()
        {
            Assert.That(BuildModeController.ResolveSnapAxis(Vector3.forward, PlacementSnapAxis.None, true), Is.EqualTo(PlacementSnapAxis.Forward));
            Assert.That(BuildModeController.ResolveSnapAxis(Vector3.back, PlacementSnapAxis.None, true), Is.EqualTo(PlacementSnapAxis.Rear));
            Assert.That(BuildModeController.ResolveSnapAxis(Vector3.left, PlacementSnapAxis.None, true), Is.EqualTo(PlacementSnapAxis.Left));
            Assert.That(BuildModeController.ResolveSnapAxis(Vector3.right, PlacementSnapAxis.None, true), Is.EqualTo(PlacementSnapAxis.Right));
            Assert.That(BuildModeController.ResolveSnapAxis(Vector3.up, PlacementSnapAxis.None, true), Is.EqualTo(PlacementSnapAxis.Top));
            Assert.That(BuildModeController.ResolveSnapAxis(Vector3.down, PlacementSnapAxis.None, true), Is.EqualTo(PlacementSnapAxis.Bottom));

            Assert.That(
                BuildModeController.ResolveSnapAxis(new Vector3(1f, 0f, 1f), PlacementSnapAxis.None, true),
                Is.EqualTo(PlacementSnapAxis.None));
        }

        [Test]
        public void PlacementSnap_UsesTwentyTwoTwentyEightDegreeHysteresisAndAltBypass()
        {
            var twentyFiveDegreesFromRear = Quaternion.AngleAxis(25f, Vector3.up) * Vector3.back;

            Assert.That(
                BuildModeController.ResolveSnapAxis(twentyFiveDegreesFromRear, PlacementSnapAxis.Rear, true),
                Is.EqualTo(PlacementSnapAxis.Rear));
            Assert.That(
                BuildModeController.ResolveSnapAxis(twentyFiveDegreesFromRear, PlacementSnapAxis.None, true),
                Is.EqualTo(PlacementSnapAxis.None));
            Assert.That(
                BuildModeController.ResolveSnapAxis(Vector3.back, PlacementSnapAxis.Rear, false),
                Is.EqualTo(PlacementSnapAxis.None));
        }

        [Test]
        public void PlacementSnap_CenterRadiusUsesTangentialEllipsoidCoordinates()
        {
            var halfExtents = new Vector3(1.5f, 1.1f, 3f);

            Assert.That(
                BuildModeController.IsWithinCenterSnap(new Vector3(0.15f, 0.11f, -2.98f), PlacementSnapAxis.Rear, halfExtents),
                Is.True);
            Assert.That(
                BuildModeController.IsWithinCenterSnap(new Vector3(0.45f, 0f, -2.85f), PlacementSnapAxis.Rear, halfExtents),
                Is.False);
            Assert.That(
                BuildModeController.IsWithinCenterSnap(new Vector3(1.45f, 0.10f, 0.30f), PlacementSnapAxis.Right, halfExtents),
                Is.True);
        }

        [Test]
        public void PlacementSnap_GridOnlyQuantizesCoordinatesAlongTheSurface()
        {
            Vector3 rear = BuildModeController.SnapTangentialToGrid(
                new Vector3(0.37f, 0.61f, -2.93f),
                PlacementSnapAxis.Rear,
                0.25f);
            Assert.That(rear.x, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(rear.y, Is.EqualTo(0.50f).Within(0.0001f));
            Assert.That(rear.z, Is.EqualTo(-2.93f).Within(0.0001f));

            Vector3 right = BuildModeController.SnapTangentialToGrid(
                new Vector3(1.47f, 0.38f, -0.62f),
                PlacementSnapAxis.Right,
                0.25f);
            Assert.That(right.x, Is.EqualTo(1.47f).Within(0.0001f));
            Assert.That(right.y, Is.EqualTo(0.50f).Within(0.0001f));
            Assert.That(right.z, Is.EqualTo(-0.50f).Within(0.0001f));

            Vector3 top = BuildModeController.SnapTangentialToGrid(
                new Vector3(-0.38f, 1.08f, 0.64f),
                PlacementSnapAxis.Top,
                0.25f);
            Assert.That(top.x, Is.EqualTo(-0.50f).Within(0.0001f));
            Assert.That(top.y, Is.EqualTo(1.08f).Within(0.0001f));
            Assert.That(top.z, Is.EqualTo(0.75f).Within(0.0001f));
        }

        [Test]
        public void PlacementSnap_CenterClearsOnlySurfaceCoordinates()
        {
            Assert.That(
                BuildModeController.ClearTangentialCoordinates(
                    new Vector3(0.4f, -0.3f, -2.9f),
                    PlacementSnapAxis.Rear),
                Is.EqualTo(new Vector3(0f, 0f, -2.9f)));
            Assert.That(
                BuildModeController.ClearTangentialCoordinates(
                    new Vector3(1.5f, -0.3f, 0.6f),
                    PlacementSnapAxis.Right),
                Is.EqualTo(new Vector3(1.5f, 0f, 0f)));
            Assert.That(
                BuildModeController.ClearTangentialCoordinates(
                    new Vector3(0.4f, 1.1f, -0.6f),
                    PlacementSnapAxis.Top),
                Is.EqualTo(new Vector3(0f, 1.1f, 0f)));
        }

        [Test]
        public void PlacementSolver_MagnetizesToNeighborEdgesWithoutForcingTheGrid()
        {
            bool snapped = SpacecraftPlacementSolver.TrySnapInterval(
                1.02f,
                0.50f,
                -0.50f,
                0.50f,
                0.10f,
                out float center,
                out PlacementAlignmentKind kind);

            Assert.That(snapped, Is.True);
            Assert.That(center, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(kind, Is.EqualTo(PlacementAlignmentKind.NeighborEdge));

            snapped = SpacecraftPlacementSolver.TrySnapInterval(
                1.18f,
                0.50f,
                -0.50f,
                0.50f,
                0.10f,
                out center,
                out kind);
            Assert.That(snapped, Is.False);
            Assert.That(center, Is.EqualTo(1.18f).Within(0.0001f));
        }

        [Test]
        public void PlacementSolver_AllowsContactButRejectsRealInterpenetration()
        {
            var left = new SpacecraftPlacementSolver.OrientedBox(
                Vector3.zero,
                Vector3.one * 0.5f,
                Quaternion.identity);
            var touching = new SpacecraftPlacementSolver.OrientedBox(
                Vector3.right,
                Vector3.one * 0.5f,
                Quaternion.identity);
            var penetrating = new SpacecraftPlacementSolver.OrientedBox(
                Vector3.right * 0.97f,
                Vector3.one * 0.5f,
                Quaternion.identity);

            Assert.That(
                SpacecraftPlacementSolver.Overlaps(left, touching, 0.012f),
                Is.False);
            Assert.That(
                SpacecraftPlacementSolver.Overlaps(left, penetrating, 0.012f),
                Is.True);
        }

        [Test]
        public void WorkshopPrefab_HasBrightFourPointRigReflectionAndSurfaceSnapSettings()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/Spacecraft/SpacecraftWorkshopRoot.prefab");
            Assert.That(prefab, Is.Not.Null);

            WorkshopLightingRig rig = prefab.GetComponentInChildren<WorkshopLightingRig>(true);
            Assert.That(rig, Is.Not.Null);
            Transform lights = rig.transform;
            Assert.That(lights.Find("KeyLight")?.GetComponent<Light>(), Is.Not.Null);
            Assert.That(lights.Find("FillLight")?.GetComponent<Light>(), Is.Not.Null);
            Assert.That(lights.Find("TopLight")?.GetComponent<Light>(), Is.Not.Null);
            Assert.That(lights.Find("RimLight")?.GetComponent<Light>(), Is.Not.Null);
            Assert.That(
                lights.Find("WorkshopReflectionProbe")?.GetComponent<ReflectionProbe>(),
                Is.Not.Null);

            BuildModeController controller =
                prefab.GetComponentInChildren<BuildModeController>(true);
            Assert.That(controller, Is.Not.Null);
            var serialized = new SerializedObject(controller);
            Assert.That(serialized.FindProperty("snappingEnabled").boolValue, Is.True);
            Assert.That(
                serialized.FindProperty("surfaceGridSize").floatValue,
                Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(
                serialized.FindProperty("neighborAlignmentDistance").floatValue,
                Is.EqualTo(0.10f).Within(0.0001f));
            Assert.That(
                serialized.FindProperty("centerSnapWorldDistance").floatValue,
                Is.EqualTo(0.10f).Within(0.0001f));
            Assert.That(
                serialized.FindProperty("gridSnapRequiresControl").boolValue,
                Is.True);
            Assert.That(
                serialized.FindProperty("allowPartSurfacePlacement").boolValue,
                Is.True);
        }

        [Test]
        public void ThrusterBinding_DefaultsToNoneAndRoundTripsThroughPartState()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(
                "Assets/SpacecraftEditor/Data/thruster_small.asset");
            var instance = Object.Instantiate(definition.Prefab);
            try
            {
                var thruster = instance.GetComponent<ThrusterPart>();
                thruster.Configure(definition, 1f, "binding-test", "mirror-test");
                Assert.That(thruster.ActivationKey, Is.EqualTo(KeyCode.None));
                Assert.That(thruster.IsBound, Is.False);

                thruster.SetActivationKey(KeyCode.W);
                var state = thruster.CaptureState();
                var clone = state.Clone();
                Assert.That(thruster.IsBound, Is.True);
                Assert.That(state.activationKey, Is.EqualTo(KeyCode.W));
                Assert.That(clone.activationKey, Is.EqualTo(KeyCode.W));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ThrusterBinding_AllowsKeyboardKeysAndRejectsFlightControlsMouseAndJoystick()
        {
            Assert.That(ThrusterKeyBinding.IsBindable(KeyCode.W), Is.True);
            Assert.That(ThrusterKeyBinding.IsBindable(KeyCode.Alpha1), Is.True);
            Assert.That(ThrusterKeyBinding.IsBindable(KeyCode.Space), Is.True);
            Assert.That(ThrusterKeyBinding.GetDisplayName(KeyCode.Alpha1), Is.EqualTo("1"));
            Assert.That(ThrusterKeyBinding.GetDisplayName(KeyCode.None), Is.EqualTo("未绑定"));

            Assert.That(ThrusterKeyBinding.IsBindable(KeyCode.Q), Is.False);
            Assert.That(ThrusterKeyBinding.IsBindable(KeyCode.E), Is.False);
            Assert.That(ThrusterKeyBinding.IsBindable(KeyCode.T), Is.False);
            Assert.That(ThrusterKeyBinding.IsBindable(KeyCode.R), Is.False);
            Assert.That(ThrusterKeyBinding.IsBindable(KeyCode.C), Is.False);
            Assert.That(ThrusterKeyBinding.IsBindable(KeyCode.Escape), Is.False);
            Assert.That(ThrusterKeyBinding.IsBindable(KeyCode.Delete), Is.False);
            Assert.That(ThrusterKeyBinding.IsBindable(KeyCode.Backspace), Is.False);
            Assert.That(ThrusterKeyBinding.IsBindable(KeyCode.Mouse0), Is.False);
            Assert.That(ThrusterKeyBinding.IsBindable(KeyCode.JoystickButton0), Is.False);
        }

        [Test]
        public void FlightThrottle_OnlyResolvesForPressedBoundKey()
        {
            Assert.That(ShipFlightController.ResolveThrottle(KeyCode.None, key => true), Is.Zero);
            Assert.That(ShipFlightController.ResolveThrottle(KeyCode.A, key => key == KeyCode.A), Is.EqualTo(1f));
            Assert.That(ShipFlightController.ResolveThrottle(KeyCode.B, key => key == KeyCode.A), Is.Zero);
            Assert.That(ShipFlightController.ResolveThrottle(KeyCode.A, null), Is.Zero);
        }

        [Test]
        public void SpacecraftBlueprint_JsonRoundTripsEveryPlacedPartField()
        {
            var original = new SpacecraftBlueprintData
            {
                hullId = "hull.spindle",
                hullMaterialId = "paint.warning_red",
                savedUtcTicks = 123456789L,
                parts = new[]
                {
                    new PlacedPartState
                    {
                        runtimeId = "part-01",
                        partId = "thruster.large",
                        localPosition = new Vector3(1f, -2f, 3f),
                        localRotation = Quaternion.Euler(10f, 20f, 30f),
                        uniformScale = 1.4f,
                        mirrorGroupId = "mirror-01",
                        activationKey = KeyCode.Alpha4,
                        materialId = "paint.gunmetal",
                        weaponGroup = 2
                    }
                }
            };

            var restored = JsonUtility.FromJson<SpacecraftBlueprintData>(JsonUtility.ToJson(original));

            Assert.That(restored.formatVersion, Is.EqualTo(2));
            Assert.That(restored.hullId, Is.EqualTo(original.hullId));
            Assert.That(restored.hullMaterialId, Is.EqualTo("paint.warning_red"));
            Assert.That(restored.savedUtcTicks, Is.EqualTo(original.savedUtcTicks));
            Assert.That(restored.parts, Has.Length.EqualTo(1));
            Assert.That(restored.parts[0].runtimeId, Is.EqualTo("part-01"));
            Assert.That(restored.parts[0].partId, Is.EqualTo("thruster.large"));
            Assert.That(restored.parts[0].localPosition, Is.EqualTo(new Vector3(1f, -2f, 3f)));
            Assert.That(Quaternion.Angle(restored.parts[0].localRotation, original.parts[0].localRotation), Is.LessThan(0.01f));
            Assert.That(restored.parts[0].uniformScale, Is.EqualTo(1.4f).Within(0.001f));
            Assert.That(restored.parts[0].mirrorGroupId, Is.EqualTo("mirror-01"));
            Assert.That(restored.parts[0].activationKey, Is.EqualTo(KeyCode.Alpha4));
            Assert.That(restored.parts[0].materialId, Is.EqualTo("paint.gunmetal"));
            Assert.That(restored.parts[0].weaponGroup, Is.EqualTo(2));
        }

        [Test]
        public void ModularCatalog_ContainsDecorationsThrustersAndBothWeaponMountModes()
        {
            var definitions = AssetDatabase.FindAssets("t:ShipPartDefinition", new[] { "Assets/SpacecraftEditor/Data" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(definition => definition != null)
                .ToArray();

            Assert.That(definitions.Count(value => value.Category == SpacecraftPartCategory.Decoration), Is.EqualTo(8));
            Assert.That(definitions.Count(value => value.Category == SpacecraftPartCategory.Thruster), Is.EqualTo(3));
            Assert.That(definitions.Count(value => value.Category == SpacecraftPartCategory.Weapon), Is.EqualTo(12));
            Assert.That(definitions.Where(value => value.Category == SpacecraftPartCategory.Weapon)
                .All(value => value.ScaleMode == SpacecraftPartScaleMode.Fixed && value.Weapon != null), Is.True);
            Assert.That(definitions.Count(value => value.Category == SpacecraftPartCategory.Weapon &&
                value.Weapon != null &&
                value.Weapon.MountMode == SpaceWeaponMountMode.Fixed), Is.EqualTo(6));
            Assert.That(definitions.Count(value => value.Category == SpacecraftPartCategory.Weapon &&
                value.Weapon != null &&
                value.Weapon.MountMode == SpaceWeaponMountMode.Gimbaled), Is.EqualTo(6));
        }

        [Test]
        public void DecorationMass_ChangesCenterOfMassWithoutAddingThrust()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(
                "Assets/SpacecraftEditor/Data/ModularParts/decor_swept_wing.asset");
            var ship = new GameObject("DecorationMassShip", typeof(Rigidbody), typeof(ShipAssembly));
            var parts = new GameObject("Parts").transform;
            parts.SetParent(ship.transform, false);
            try
            {
                var assembly = ship.GetComponent<ShipAssembly>();
                assembly.Configure(ship.GetComponent<Rigidbody>(), parts, null, 100f);
                SpacecraftPart part = assembly.AddGenericPart(definition, new Vector3(2f, 0f, 0f), Quaternion.identity, 1f);

                Assert.That(part, Is.TypeOf<DecorationPart>());
                Assert.That(assembly.Metrics.totalMass, Is.EqualTo(108f).Within(0.001f));
                Assert.That(assembly.Metrics.localCenterOfMass.x, Is.EqualTo(16f / 108f).Within(0.001f));
                Assert.That(assembly.Metrics.localResultantForce, Is.EqualTo(Vector3.zero));
                Assert.That(assembly.Thrusters, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(ship);
            }
        }

        [Test]
        public void WeaponDefinition_EnforcesAuthoredScaleAndMuzzleForward()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(
                "Assets/SpacecraftEditor/Data/ModularParts/weapon_kinetic_repeater_s3.asset");
            var instance = Object.Instantiate(definition.Prefab);
            try
            {
                var weapon = instance.GetComponent<WeaponPart>();
                weapon.Configure(definition, 0.1f, "weapon-test", "mirror-test", paintId: "paint.deep_space_blue", weaponGroup: 2);
                Transform muzzle = weapon.PrimaryMuzzle;

                Assert.That(weapon.UniformScale, Is.EqualTo(definition.FixedScale).Within(0.001f));
                Assert.That(weapon.FireGroup, Is.EqualTo(2));
                Assert.That(muzzle, Is.Not.Null);
                Assert.That(Vector3.Dot(muzzle.forward, instance.transform.up), Is.GreaterThan(0.999f));
                Assert.That(weapon.CaptureState().weaponGroup, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [TestCase("weapon_kinetic_repeater_gimbal_s1", SpaceWeaponMountSize.S1)]
        [TestCase("weapon_kinetic_repeater_gimbal_s2", SpaceWeaponMountSize.S2)]
        [TestCase("weapon_kinetic_repeater_gimbal_s3", SpaceWeaponMountSize.S3)]
        [TestCase("weapon_energy_pulse_gimbal_s1", SpaceWeaponMountSize.S1)]
        [TestCase("weapon_energy_pulse_gimbal_s2", SpaceWeaponMountSize.S2)]
        [TestCase("weapon_energy_pulse_gimbal_s3", SpaceWeaponMountSize.S3)]
        public void GimbaledWeaponDefinitions_HaveStableTrackingAndMovableMuzzles(
            string assetName,
            SpaceWeaponMountSize expectedSize)
        {
            var definition = AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(
                "Assets/SpacecraftEditor/Data/ModularParts/" + assetName + ".asset");
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Weapon.MountMode, Is.EqualTo(SpaceWeaponMountMode.Gimbaled));
            Assert.That(definition.Weapon.MountSize, Is.EqualTo(expectedSize));
            Assert.That(definition.Weapon.GimbalConeDegrees, Is.EqualTo(18f).Within(0.01f));
            Assert.That(definition.Weapon.GimbalTrackingDegreesPerSecond, Is.EqualTo(120f).Within(0.01f));

            var instance = Object.Instantiate(definition.Prefab);
            try
            {
                var weapon = instance.GetComponent<WeaponPart>();
                weapon.Configure(definition, 1f);
                Vector3 initialDirection = weapon.PrimaryMuzzle.forward;
                weapon.ApplyAimDirection((instance.transform.up + instance.transform.right * 0.2f).normalized);
                Assert.That(Vector3.Angle(initialDirection, weapon.PrimaryMuzzle.forward), Is.GreaterThan(1f));
                Assert.That(instance.transform.Find("GimbalPivot"), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void PirateHardpointLayouts_SeparateThrustersWeaponsAndDecorations()
        {
            string[] hullIds = { "hull.balanced", "hull.saucer", "hull.spindle" };
            foreach (string hullId in hullIds)
            {
                string safeId = hullId.Replace('.', '_');
                var layout = AssetDatabase.LoadAssetAtPath<SpacecraftHardpointLayout>(
                    "Assets/Resources/Spaceflight/Pirates/Hardpoints_" + safeId + ".asset");
                Assert.That(layout, Is.Not.Null, hullId);
                Assert.That(layout.HullId, Is.EqualTo(hullId));
                Assert.That(layout.Hardpoints.Count(point => point.Category == SpacecraftPartCategory.Thruster),
                    Is.GreaterThanOrEqualTo(2), hullId);
                Assert.That(layout.Hardpoints.Count(point => point.Category == SpacecraftPartCategory.Weapon),
                    Is.GreaterThanOrEqualTo(2), hullId);
                Assert.That(layout.DecorationRegions, Is.Not.Empty, hullId);

                foreach (SpacecraftHardpoint point in layout.Hardpoints)
                {
                    Assert.That(point.MirrorHardpointId, Is.Not.Empty, point.HardpointId);
                    Assert.That(layout.Hardpoints.Any(candidate =>
                        candidate.HardpointId == point.MirrorHardpointId), Is.True, point.HardpointId);
                }
            }
        }

        [Test]
        public void SpacecraftWorkshop_IsAValidStandaloneSceneOutsideBuildSettings()
        {
            const string scenePath = "Assets/Scenes/SpacecraftWorkshop.unity";
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath), Is.Not.Null);
            Assert.That(EditorBuildSettings.scenes.Any(scene => scene.path == scenePath), Is.False);

            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                var root = scene.GetRootGameObjects().Single(gameObject => gameObject.name == "GameRoot");
                Assert.That(root.transform.Find("ShipRoot"), Is.Not.Null);
                Assert.That(root.transform.Find("EditorSystems"), Is.Not.Null);
                Assert.That(root.transform.Find("WorkshopCanvas/BuildInterface"), Is.Not.Null);
                Assert.That(root.transform.Find("WorkshopCanvas/FlightHud"), Is.Not.Null);
                Assert.That(root.transform.Find("WorkshopCanvas/HullSelectionPanel"), Is.Not.Null);
                Assert.That(root.GetComponentInChildren<WorkshopUIReferences>(true).IsConfigured, Is.True);
            }
            finally
            {
                if (openedForTest)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void InterstellarFlight_DoesNotContainVisibleFlightControlMarkers()
        {
            const string scenePath = "Assets/Scenes/InterstellarFlight.unity";
            var scene = SceneManager.GetSceneByPath(scenePath);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                string[] names = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .Select(item => item.name)
                    .ToArray();
                Assert.That(names, Does.Not.Contain("VJoyBoundary"));
                Assert.That(names, Does.Not.Contain("VJoyCursor"));
                Assert.That(names, Does.Not.Contain("VelocityMarker"));
            }
            finally
            {
                if (openedForTest)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void InterstellarFlight_HudKeepsStatusAndWeaponsClearOfTheShip()
        {
            const string scenePath = "Assets/Scenes/InterstellarFlight.unity";
            var scene = SceneManager.GetSceneByPath(scenePath);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                Transform[] transforms = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .ToArray();
                RectTransform FindRect(string name) => transforms
                    .Single(item => item.name == name)
                    .GetComponent<RectTransform>();

                RectTransform integrity = FindRect("IntegrityWidget");
                Assert.That(integrity.anchorMax.x, Is.LessThanOrEqualTo(0.95f));

                string[] statusNames =
                {
                    "SpeedText", "FlightModeText", "AuthorityText", "SpeedLimitText", "BoostText"
                };
                RectTransform previous = null;
                foreach (string statusName in statusNames)
                {
                    RectTransform current = FindRect(statusName);
                    Assert.That(current.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
                    Assert.That(current.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
                    if (previous != null)
                    {
                        float verticalGap = previous.anchoredPosition.y - current.anchoredPosition.y;
                        Assert.That(verticalGap, Is.GreaterThanOrEqualTo(previous.sizeDelta.y));
                    }
                    previous = current;
                }

                RectTransform weaponHud = FindRect("WeaponHud");
                Assert.That(weaponHud.anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(weaponHud.anchorMax, Is.EqualTo(Vector2.one));
                foreach (string weaponTextName in new[]
                {
                    "WeaponGroupText", "WeaponAmmoText", "WeaponCapacitorText",
                    "WeaponHeatText", "WeaponMountText", "WeaponLockText"
                })
                {
                    RectTransform weaponText = FindRect(weaponTextName);
                    Assert.That(weaponText.anchorMin.x, Is.EqualTo(1f));
                    Assert.That(weaponText.anchoredPosition.x, Is.LessThanOrEqualTo(-24f));
                }
            }
            finally
            {
                if (openedForTest)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

    }
}
