using ModularAssembly;
using NUnit.Framework;
using SpacecraftEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.IcePlanet;
using UnityPlanet.ModularAssembly;

public sealed class VehicleRuntimeOptimizationTests
{
    sealed class CountingEnvironmentProvider :
        IPlanetEnvironmentProvider,
        IPlanetAirEnvironmentProvider
    {
        public int fullSamples;
        public int airflowSamples;
        public bool ForceNoWind { get; set; }

        public PlanetEnvironmentSample Sample(
            Vector3 worldPosition,
            double simulationTime)
        {
            fullSamples++;
            return CreateSample();
        }

        public PlanetEnvironmentSample SampleAirflow(
            Vector3 worldPosition,
            double simulationTime)
        {
            airflowSamples++;
            return CreateSample();
        }

        static PlanetEnvironmentSample CreateSample()
        {
            return new PlanetEnvironmentSample
            {
                airDensity = 0.82f,
                atmosphereVelocity = new Vector3(3f, -1f, 2f),
                hasAtmosphere = true,
                hasSurface = true,
                surfaceDistance = 12f,
                surfaceNormal = Vector3.up
            };
        }
    }

    [Test]
    public void AirflowUsesFastSampleAndReusesItForRelativeVelocity()
    {
        var provider = new CountingEnvironmentProvider();
        var airflow = new VehicleAirflowField();
        airflow.Configure(
            provider,
            default,
            null,
            null);

        PlanetEnvironmentSample sample = airflow.Sample(Vector3.one);
        Assert.AreEqual(0, provider.fullSamples);
        Assert.AreEqual(1, provider.airflowSamples);

        var bodyObject = new GameObject("AirflowSampleBody");
        try
        {
            Rigidbody body = bodyObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            Vector3 relative = airflow.RelativeAirVelocity(
                body,
                Vector3.one,
                "test-face",
                sample);

            Assert.AreEqual(0, provider.fullSamples);
            Assert.AreEqual(1, provider.airflowSamples);
            Assert.That(relative.x, Is.EqualTo(-3f).Within(0.00001f));
            Assert.That(relative.y, Is.EqualTo(1f).Within(0.00001f));
            Assert.That(relative.z, Is.EqualTo(-2f).Within(0.00001f));
        }
        finally
        {
            Object.DestroyImmediate(bodyObject);
        }
    }

    [Test]
    public void IcePlanetConditionsPreserveFastAirflowSampling()
    {
        var provider = new CountingEnvironmentProvider();
        var conditionsObject = new GameObject("IceConditionsFastAirTest");
        try
        {
            var area = conditionsObject.AddComponent<IcePlanetCombatArea>();
            var conditions =
                conditionsObject.AddComponent<IcePlanetEnvironmentConditions>();
            SetPrivateField(area, "arenaSize", new Vector2(1000f, 1000f));
            SetPrivateField(conditions, "source", provider);
            SetPrivateField(conditions, "combatArea", area);
            SetPrivateField(conditions, "seed", 7341);
            SetPrivateField(conditions, "densityMultiplier", 1.09f);
            SetPrivateField(conditions, "windMultiplier", 1.35f);
            SetPrivateField(conditions, "crossWindSpeed", 9f);
            SetPrivateField(conditions, "additionalGustSpeed", 7f);

            Assert.That(
                conditions,
                Is.InstanceOf<IPlanetAirEnvironmentProvider>());
            PlanetEnvironmentSample fullSample =
                conditions.Sample(Vector3.one, 12d);
            PlanetEnvironmentSample airflowSample =
                conditions.SampleAirflow(Vector3.one, 12d);

            Assert.AreEqual(1, provider.fullSamples);
            Assert.AreEqual(1, provider.airflowSamples);
            Assert.That(
                airflowSample.airDensity,
                Is.EqualTo(fullSample.airDensity).Within(0.000001f));
            Assert.AreEqual(
                fullSample.meanWindVelocity,
                airflowSample.meanWindVelocity);
            Assert.AreEqual(
                fullSample.gustVelocity,
                airflowSample.gustVelocity);
            Assert.AreEqual(
                fullSample.atmosphereVelocity,
                airflowSample.atmosphereVelocity);
        }
        finally
        {
            Object.DestroyImmediate(conditionsObject);
        }
    }

    [Test]
    public void CoherentVehicleAirflowRetainsBuildDragAndTorqueDifferences()
    {
        var provider = new CountingEnvironmentProvider();
        var airflow = new VehicleAirflowField();
        PlanetEnvironmentSample environment =
            PlanetEnvironmentSample.EarthLike(Vector3.down * 9.81f, 20f);
        environment.airDensity = 1f;
        environment.meanWindVelocity = Vector3.zero;
        environment.gustVelocity = Vector3.zero;
        environment.atmosphereVelocity = Vector3.zero;
        airflow.Configure(provider, environment, null, null);

        var bodyObject = new GameObject("CoherentAirflowBuildTest");
        try
        {
            Rigidbody body = bodyObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.velocity = Vector3.right * 20f;

            var compact = new VehiclePhysicsRc3State();
            AddExposedFace(compact, "core", Vector3.zero, 1f);
            var compactLedger = new VehicleForceLedger();
            compactLedger.Begin(body);
            compact.AccumulateAerodynamics(
                body,
                bodyObject.transform,
                environment.airDensity,
                airflow,
                environment,
                compactLedger);

            var modified = new VehiclePhysicsRc3State();
            AddExposedFace(modified, "core", Vector3.zero, 1f);
            AddExposedFace(
                modified,
                "offset-armor",
                Vector3.up * 2f,
                1f);
            var modifiedLedger = new VehicleForceLedger();
            modifiedLedger.Begin(body);
            modified.AccumulateAerodynamics(
                body,
                bodyObject.transform,
                environment.airDensity,
                airflow,
                environment,
                modifiedLedger);

            Assert.AreEqual(0, provider.fullSamples);
            Assert.AreEqual(0, provider.airflowSamples);
            Assert.That(
                modified.Snapshot.currentDrag,
                Is.GreaterThan(compact.Snapshot.currentDrag * 1.9f));
            Assert.That(
                Mathf.Abs(modified.Snapshot.aerodynamicTorqueLocal.z),
                Is.GreaterThan(100f));
        }
        finally
        {
            Object.DestroyImmediate(bodyObject);
        }
    }

    [Test]
    public void BufferedRcsCompletionMatchesSnapshotCompletion()
    {
        var input = new[]
        {
            new Rcs24ThrusterInput
            {
                sourceIndex = 0,
                localPosition = new Vector3(1f, 0f, -2f),
                localDirection = Vector3.forward,
                maximumForce = 120f,
                responseTime = 0.1f
            }
        };
        var snapshotAllocator = new VirtualRcs24Allocator();
        var bufferedAllocator = new VirtualRcs24Allocator();
        snapshotAllocator.Rebuild(
            input,
            Vector3.zero,
            false,
            0f,
            0f,
            0f);
        bufferedAllocator.Rebuild(
            input,
            Vector3.zero,
            false,
            0f,
            0f,
            0f);

        var scales = new[] { 0.75f };
        var request = new Rcs24SolveRequest
        {
            desired = Vector3.forward * 60f,
            strictDirection = true,
            group = "buffer-equivalence"
        };
        snapshotAllocator.BeginStep(scales);
        bufferedAllocator.BeginStep(scales);
        snapshotAllocator.SolveTranslation(request);
        bufferedAllocator.SolveTranslation(request);

        Rcs24SolveResult snapshot =
            snapshotAllocator.CompleteStep(0.02f);
        Rcs24SolveResult buffered =
            bufferedAllocator.CompleteStepBuffered(0.02f);

        Assert.AreEqual(snapshot.localForce, buffered.localForce);
        Assert.AreEqual(snapshot.localTorque, buffered.localTorque);
        CollectionAssert.AreEqual(
            snapshot.thrusterThrottles,
            buffered.thrusterThrottles);

        float retainedSnapshot = snapshot.thrusterThrottles[0];
        bufferedAllocator.BeginStep(scales);
        Rcs24SolveResult nextBuffered =
            bufferedAllocator.CompleteStepBuffered(0.02f);
        Assert.AreSame(
            buffered.thrusterThrottles,
            nextBuffered.thrusterThrottles);
        Assert.AreEqual(
            retainedSnapshot,
            snapshot.thrusterThrottles[0]);
    }

    [Test]
    public void RcsScratchBuffersRetainKnownMultiAxisWrenchBaseline()
    {
        var inputs = new[]
        {
            Thruster(0, new Vector3(-2f, 0f, -3f),
                new Vector3(0f, 1f, 0.25f), 120f, 0.12f),
            Thruster(1, new Vector3(2f, 0f, -3f),
                new Vector3(0f, 1f, 0.25f), 120f, 0.12f),
            Thruster(2, new Vector3(-2f, 0f, 3f),
                new Vector3(0f, 1f, -0.25f), 120f, 0.12f),
            Thruster(3, new Vector3(2f, 0f, 3f),
                new Vector3(0f, 1f, -0.25f), 120f, 0.12f),
            Thruster(4, new Vector3(-2f, 0f, -2f),
                Vector3.right, 80f, 0.08f),
            Thruster(5, new Vector3(2f, 0f, 2f),
                Vector3.left, 80f, 0.08f),
            Thruster(6, new Vector3(-2f, 0f, 2f),
                Vector3.forward, 90f, 0.1f),
            Thruster(7, new Vector3(2f, 0f, -2f),
                Vector3.back, 90f, 0.1f)
        };
        var allocator = new VirtualRcs24Allocator();
        allocator.Rebuild(
            inputs,
            new Vector3(0.15f, -0.2f, 0.35f),
            false,
            0f,
            0f,
            0f);
        allocator.BeginStep(new[]
        {
            1f, 0.85f, 0.95f, 0.7f, 1f, 0.6f, 0.9f, 0.8f
        });
        allocator.SolveRotation(new Rcs24SolveRequest
        {
            desired = new Vector3(35f, -18f, 22f),
            strictDirection = false,
            group = "baseline-rotation"
        });
        allocator.SolveTranslation(new Rcs24SolveRequest
        {
            desired = new Vector3(42f, 150f, -28f),
            strictDirection = false,
            group = "baseline-translation"
        });

        Rcs24SolveResult result = allocator.CompleteStepBuffered(0.02f);

        AssertVector(
            new Vector3(9.290367f, 22.9234962f, -5.675398f),
            result.localForce);
        AssertVector(
            new Vector3(46.7091827f, 1.53092957f, -39.6263428f),
            result.localTorque);
        Assert.AreEqual(1f, result.commonScale);
        Assert.AreEqual(0, result.missingAxisMask);
        CollectionAssert.AreEqual(
            new[]
            {
                0.137280673f,
                0.0213912949f,
                0.0367314555f,
                0.009357311f,
                0.119333036f,
                0.00533909f,
                0.00607854826f,
                0.131752491f
            },
            result.thrusterThrottles);
    }

    [Test]
    public void ArcadeAssist_ReleasingMovementBrakesAndHoldsPosition()
    {
        TrainingFlightAssistDemand demand =
            TrainingFlightAssist.CalculateDemand(
                new Vector3(4f, 8f, -3f),
                new Vector3(12f, -3f, 5f),
                Vector3.zero,
                false,
                Vector3.zero,
                30f);

        Assert.That(
            Vector3.Dot(
                demand.controlAccelerationWorld,
                new Vector3(12f, -3f, 5f)),
            Is.LessThan(0f));
        Assert.That(demand.controlAccelerationWorld.y, Is.GreaterThan(0f));
        AssertVector(new Vector3(4f, 8f, -3f), demand.holdPosition);
    }

    [Test]
    public void ArcadeAssist_StrafeIntentIsSeparatedFromForwardDrift()
    {
        Vector3 velocity = Vector3.forward * 24f;
        TrainingFlightAssistDemand demand =
            TrainingFlightAssist.CalculateDemand(
                Vector3.zero,
                velocity,
                Vector3.zero,
                true,
                Vector3.right,
                18f);

        Assert.That(
            Vector3.Dot(
                demand.intentAccelerationWorld,
                Vector3.right),
            Is.GreaterThan(0f));
        Assert.That(
            Mathf.Abs(Vector3.Dot(
                demand.intentAccelerationWorld,
                Vector3.forward)),
            Is.LessThan(0.0001f));
        Assert.That(
            Vector3.Dot(
                demand.driftCancellationAccelerationWorld,
                velocity),
            Is.LessThan(0f));
        AssertVector(
            (demand.targetVelocityWorld - velocity) /
            TrainingFlightAssist.VelocityResponseSeconds,
            demand.controlAccelerationWorld);
    }

    [TestCase(1f, 0f, 0f)]
    [TestCase(-1f, 0f, 0f)]
    [TestCase(0f, 0f, -1f)]
    [TestCase(0f, 1f, 0f)]
    [TestCase(1f, 0f, -1f)]
    public void ArcadeAssist_NewDirectionReceivesPrimaryDemand(
        float x,
        float y,
        float z)
    {
        Vector3 direction = new Vector3(x, y, z).normalized;
        TrainingFlightAssistDemand demand =
            TrainingFlightAssist.CalculateDemand(
                Vector3.zero,
                Vector3.forward * 22f,
                Vector3.zero,
                true,
                direction,
                18f);

        Assert.That(
            Vector3.Dot(
                demand.intentAccelerationWorld,
                direction),
            Is.GreaterThan(0f));
    }

    [Test]
    public void ArcadeAssist_ReleaseStopIsStrongButLegacyDemandIsStable()
    {
        Vector3 position = new Vector3(2f, -1f, 0.5f);
        Vector3 velocity = new Vector3(8f, -2f, 3f);
        TrainingFlightAssistDemand demand =
            TrainingFlightAssist.CalculateDemand(
                position,
                velocity,
                Vector3.zero,
                true,
                Vector3.zero,
                30f);

        Assert.That(
            Vector3.Dot(demand.stopAccelerationWorld, velocity),
            Is.LessThan(0f));
        Assert.That(
            demand.stopAccelerationWorld.magnitude,
            Is.EqualTo(
                velocity.magnitude *
                TrainingFlightAssist.StopVelocityGain)
                .Within(0.001f));
        Assert.That(
            demand.positionHoldAccelerationWorld.x,
            Is.LessThan(0f));
        AssertVector(
            -position * 5f - velocity * 4.6f,
            demand.controlAccelerationWorld);
    }

    [Test]
    public void ArcadeAssist_IntentPriorityOnlyAppliesToDirectPlayerTraining()
    {
        Assert.IsTrue(
            TrainingFlightAssist.ShouldPrioritizePlayerIntent(
                VehicleCoreAssistMode.Training,
                true,
                false));
        Assert.IsFalse(
            TrainingFlightAssist.ShouldPrioritizePlayerIntent(
                VehicleCoreAssistMode.Standard,
                true,
                false));
        Assert.IsFalse(
            TrainingFlightAssist.ShouldPrioritizePlayerIntent(
                VehicleCoreAssistMode.Training,
                true,
                true));
        Assert.IsFalse(
            TrainingFlightAssist.ShouldPrioritizePlayerIntent(
                VehicleCoreAssistMode.Training,
                false,
                false));
    }

    [Test]
    public void ArcadeTuningProfile_ChangesPlayerResponseAndTargetSpeed()
    {
        var tuning = ScriptableObject.CreateInstance<
            ArcadeFlightTuningProfile>();
        try
        {
            tuning.ResetToDefaults();
            tuning.intentResponseSeconds = 0.08f;
            tuning.driftResponseSeconds = 0.64f;
            tuning.baseTargetSpeed = 20f;
            tuning.minimumTargetSpeed = 20f;
            tuning.maximumTargetSpeed = 100f;
            tuning.accelerationToSpeed = 5f;

            TrainingFlightAssistDemand demand =
                TrainingFlightAssist.CalculateDemand(
                    Vector3.zero,
                    Vector3.forward * 24f,
                    Vector3.zero,
                    true,
                    Vector3.right,
                    18f,
                    tuning);

            Assert.That(
                demand.intentAccelerationWorld.x,
                Is.EqualTo(18f / 0.08f).Within(0.001f));
            Assert.That(
                demand.driftCancellationAccelerationWorld.z,
                Is.EqualTo(-24f / 0.64f).Within(0.001f));
            Assert.That(
                TrainingFlightAssist.CalculateTargetSpeed(
                    8f,
                    false,
                    tuning),
                Is.EqualTo(60f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(tuning);
        }
    }

    [Test]
    public void ArcadeAssist_DescentUsesDedicatedResponseAndAuthority()
    {
        var tuning = ScriptableObject.CreateInstance<
            ArcadeFlightTuningProfile>();
        try
        {
            tuning.ResetToDefaults();
            tuning.intentResponseSeconds = 0.24f;
            tuning.intentAuthorityFraction = 0.55f;
            tuning.descentResponseSeconds = 0.06f;
            tuning.descentAuthorityFraction = 0.96f;

            Vector3 descent = Vector3.down;
            Assert.That(
                TrainingFlightAssist.ResolveIntentResponseSeconds(
                    descent,
                    Vector3.up,
                    tuning),
                Is.EqualTo(0.06f).Within(0.0001f));
            Assert.That(
                TrainingFlightAssist.ResolveIntentAuthorityFraction(
                    descent,
                    Vector3.up,
                    tuning),
                Is.EqualTo(0.96f).Within(0.0001f));
            Assert.That(
                TrainingFlightAssist.ResolveIntentResponseSeconds(
                    Vector3.forward,
                    Vector3.up,
                    tuning),
                Is.EqualTo(0.24f).Within(0.0001f));
            Assert.That(
                TrainingFlightAssist.ResolveIntentAuthorityFraction(
                    Vector3.forward,
                    Vector3.up,
                    tuning),
                Is.EqualTo(0.55f).Within(0.0001f));

            TrainingFlightAssistDemand demand =
                TrainingFlightAssist.CalculateDemand(
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.zero,
                    true,
                    descent,
                    18f,
                    tuning,
                    TrainingFlightAssist.ResolveIntentResponseSeconds(
                        descent,
                        Vector3.up,
                        tuning));
            Assert.That(
                demand.intentAccelerationWorld.y,
                Is.EqualTo(-300f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(tuning);
        }
    }

    [Test]
    public void ArcadeAssist_DescentFallbackScalesWithVehicleMass()
    {
        var tuning = ScriptableObject.CreateInstance<
            ArcadeFlightTuningProfile>();
        try
        {
            tuning.ResetToDefaults();
            tuning.minimumDescentAcceleration = 6f;

            Assert.That(
                TrainingFlightAssist.CalculateDescentAssistForce(
                    1000f,
                    3000f,
                    tuning),
                Is.EqualTo(6000f).Within(0.001f));
            Assert.That(
                TrainingFlightAssist.CalculateDescentAssistForce(
                    100f,
                    3000f,
                    tuning),
                Is.EqualTo(3000f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(tuning);
        }
    }

    [Test]
    public void ArcadeTuningProfile_DefaultResourceIsAvailable()
    {
        ArcadeFlightTuningProfile tuning =
            ArcadeFlightTuningProfile.Load();

        Assert.NotNull(tuning);
        Assert.That(
            tuning.intentAuthorityFraction,
            Is.EqualTo(0.75f).Within(0.0001f));
        Assert.That(
            tuning.stopVelocityGain,
            Is.EqualTo(10f).Within(0.0001f));
        Assert.That(
            tuning.descentResponseSeconds,
            Is.EqualTo(0.08f).Within(0.0001f));
        Assert.That(
            tuning.descentAuthorityFraction,
            Is.EqualTo(1f).Within(0.0001f));
        Assert.That(
            tuning.minimumDescentAcceleration,
            Is.EqualTo(6f).Within(0.0001f));
    }

    [Test]
    public void LegacyNaturalTestFlightSelectionCanonicalizesToCity()
    {
        Assert.That(
            (int)FlightEnvironmentKind.Natural,
            Is.EqualTo((int)FlightEnvironmentKind.PlanetLab));
        Assert.That(
            (int)FlightEnvironmentKind.City,
            Is.EqualTo((int)FlightEnvironmentKind.CombatMapLab));

        Assert.That(
            FlightEnvironmentManager.CanonicalizeSelectedKind(
                FlightEnvironmentKind.Natural),
            Is.EqualTo(FlightEnvironmentKind.City));
        Assert.That(
            FlightEnvironmentManager.CanonicalizeSelectedKind(
                FlightEnvironmentKind.PlanetLab),
            Is.EqualTo(FlightEnvironmentKind.City));
        Assert.That(
            FlightEnvironmentManager.CanonicalizeSelectedKind(
                (FlightEnvironmentKind)999),
            Is.EqualTo(FlightEnvironmentKind.City));
    }

    [Test]
    public void TestFlightSelectorOffersCityOnly()
    {
        var host = new GameObject("CityOnlyFlightSelectorTest");
        try
        {
            FlightEnvironmentSelectorOverlay overlay =
                host.AddComponent<FlightEnvironmentSelectorOverlay>();
            System.Reflection.MethodInfo buildUi =
                typeof(FlightEnvironmentSelectorOverlay).GetMethod(
                    "BuildUi",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
            Assert.That(buildUi, Is.Not.Null);
            buildUi.Invoke(overlay, null);

            UnityEngine.UI.Button[] buttons =
                host.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            Assert.That(buttons, Is.Empty);

            bool foundFixedCityCard = false;
            UnityEngine.UI.Text[] labels =
                host.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            for (int index = 0; index < labels.Length; index++)
            {
                string value = labels[index].text ?? string.Empty;
                Assert.That(value, Does.Not.Contain("自然"));
                if (value == "城市 PCG 试飞场（固定）")
                    foundFixedCityCard = true;
            }
            Assert.That(foundFixedCityCard, Is.True);
            Assert.That(
                host.transform.Find(
                    "FlightEnvironmentSelectorCanvas/" +
                    "CityFlightEnvironmentPanel/城市PCG试飞场_固定"),
                Is.Not.Null);
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }

    [Test]
    public void ModularAssemblyLabProfileIsCityOnly()
    {
        const string path = "Assets/Scenes/ModularAssemblyLab.unity";
        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedForTest = !scene.IsValid() || !scene.isLoaded;
        if (openedForTest)
        {
            scene = EditorSceneManager.OpenScene(
                path,
                OpenSceneMode.Additive);
        }

        try
        {
            Assert.That(
                ModularLabSceneProfile.AllowsPlanetLabFlightEnvironment(
                    scene),
                Is.False);
            Assert.That(
                FlightEnvironmentManager
                    .RequiresCityTestFlightEnvironment(scene),
                Is.True);

            int naturalProviderCount = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                naturalProviderCount += roots[index]
                    .GetComponentsInChildren<
                        PlanetLabFlightEnvironmentController>(true)
                    .Length;
            }
            Assert.That(naturalProviderCount, Is.Zero);
        }
        finally
        {
            if (openedForTest && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void CityTestFlightProviderRemainsAvailable()
    {
        Assert.That(
            Resources.Load<GameObject>(
                "PlanetSurface/UrbanCombatCityTemplate"),
            Is.Not.Null);

        var host = new GameObject("CityFlightProviderTest");
        try
        {
            CityTestFlightEnvironmentController provider =
                host.AddComponent<CityTestFlightEnvironmentController>();
            Assert.That(provider, Is.InstanceOf<IGridFlightEnvironment>());
            Assert.That(provider, Is.InstanceOf<IGridFlightEnvironmentWarmup>());
            Assert.That(provider, Is.InstanceOf<ICombatArenaProvider>());
            Assert.That(provider.IsReady, Is.False);
            Assert.That(
                host.GetComponentsInChildren<
                    UnityPlanet.CityPcg.AirCombatCityPcgLab>(true),
                Is.Empty,
                "创建城市试飞 Provider 本身不得提前生成城市。");
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }

    [Test]
    public void ArcadeTuningProfile_RuntimeCopyPersistsWithoutMutatingAsset()
    {
        bool hadSavedOverride = PlayerPrefs.HasKey(
            ArcadeFlightTuningProfile.RuntimePreferencesKey);
        string savedOverride = hadSavedOverride
            ? PlayerPrefs.GetString(
                ArcadeFlightTuningProfile.RuntimePreferencesKey)
            : string.Empty;
        ArcadeFlightTuningProfile source =
            ArcadeFlightTuningProfile.Load();
        float sourceIntentResponse = source.intentResponseSeconds;
        ArcadeFlightTuningProfile edited = null;
        ArcadeFlightTuningProfile restored = null;
        try
        {
            edited = ArcadeFlightTuningProfile.CreateRuntimeCopy(false);
            Assert.AreNotSame(source, edited);
            edited.ApplyPreset(ArcadeFlightTuningPreset.Responsive);
            edited.SaveRuntimeOverrides();

            restored = ArcadeFlightTuningProfile.CreateRuntimeCopy(true);

            Assert.That(
                restored.intentResponseSeconds,
                Is.EqualTo(0.08f).Within(0.0001f));
            Assert.That(
                restored.stopVelocityGain,
                Is.EqualTo(14f).Within(0.0001f));
            Assert.That(
                source.intentResponseSeconds,
                Is.EqualTo(sourceIntentResponse).Within(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(edited);
            Object.DestroyImmediate(restored);
            if (hadSavedOverride)
            {
                PlayerPrefs.SetString(
                    ArcadeFlightTuningProfile.RuntimePreferencesKey,
                    savedOverride);
            }
            else
            {
                PlayerPrefs.DeleteKey(
                    ArcadeFlightTuningProfile.RuntimePreferencesKey);
            }
            PlayerPrefs.Save();
        }
    }

    [Test]
    public void AirBuildPalette_UsesOnlyRequestedFiveCategories()
    {
        CollectionAssert.AreEqual(
            new[] { "结构", "机翼", "推进", "能源", "武器" },
            AirBuildCatalog.PaletteCategories);
        CollectionAssert.DoesNotContain(
            AirBuildCatalog.PaletteCategories,
            "移动");
        CollectionAssert.DoesNotContain(
            AirBuildCatalog.PaletteCategories,
            "全部");
        CollectionAssert.DoesNotContain(
            AirBuildCatalog.PaletteCategories,
            "防御");
    }

    [Test]
    public void AirBuildPalette_FiltersEnergyAndStructureWithoutRemovingCatalogDefinitions()
    {
        Assert.IsTrue(AirBuildCatalog.IsVisibleInPalette(
            PaletteRecord("core_heavy_222", "Core")));
        Assert.IsTrue(AirBuildCatalog.IsVisibleInPalette(
            PaletteRecord("block_111", "Structure")));
        Assert.IsTrue(AirBuildCatalog.IsVisibleInPalette(
            PaletteRecord("block_1_2_111", "Structure")));
        Assert.IsFalse(AirBuildCatalog.IsVisibleInPalette(
            PaletteRecord("assemble_222", "Structure")));

        Assert.IsTrue(AirBuildCatalog.IsVisibleInPalette(
            PaletteRecord("core_energy_111", "Core")));
        Assert.IsFalse(AirBuildCatalog.IsVisibleInPalette(
            PaletteRecord("fire_energy_storage_422", "Energy")));
        Assert.IsFalse(AirBuildCatalog.IsVisibleInPalette(
            PaletteRecord("radar_222", "Radar")));

        Assert.IsTrue(AirBuildCatalog.IsVisibleInPalette(
            PaletteRecord("rocket_222", "Thruster")));
        Assert.IsTrue(AirBuildCatalog.IsVisibleInPalette(
            PaletteRecord("large_wing_left_361", "Wing")));
        Assert.IsTrue(AirBuildCatalog.IsVisibleInPalette(
            PaletteRecord("waste_rudder", "ControlSurface")));
        Assert.IsTrue(AirBuildCatalog.IsVisibleInPalette(
            PaletteRecord("machinegun_111", "Cannon")));
        Assert.IsFalse(AirBuildCatalog.IsVisibleInPalette(
            PaletteRecord("shield_121", "Shield")));
        Assert.IsFalse(AirBuildCatalog.IsVisibleInPalette(
            PaletteRecord("wheel_basic_111", "Wheel")));

        Assert.That(
            AirBuildCatalog.OrderedIds,
            Does.Contain("wheel_basic_111"),
            "目录过滤不能删除定义；已有飞船仍需解析隐藏模块。");
    }

    [TestCase("speed_rocketsmall_112", 44000f)]
    [TestCase("small_propeller_224", 120000f)]
    [TestCase("rocket_222", 120000f)]
    public void CatalogDefinition_ReportsActualPlayerThrusterForce(
        string neoXId,
        float expectedForce)
    {
        string sourceId = "block:common:" + neoXId;
        string json =
            "{\"items\":[{" +
            "\"sourceId\":\"" + sourceId + "\"," +
            "\"neoXId\":\"" + neoXId + "\"," +
            "\"chineseName\":\"test thruster\"," +
            "\"contentKind\":\"module\"," +
            "\"ownership\":\"base\"," +
            "\"behavior\":\"Thruster\"," +
            "\"explicitFootprint\":true," +
            "\"footprint\":[1,1,1]}]}";
        ModularContentCatalog catalog =
            ModularContentCatalog.FromJson(json);
        var model = new GridAssemblyModel(
            System.Array.Empty<GridModuleDefinition>());

        try
        {
            NeoXCatalogIntegration.RegisterDefinitions(model, catalog);

            Assert.That(
                model.Definitions.TryGetValue(
                    "neox@" + sourceId,
                    out GridModuleDefinition definition),
                Is.True);
            Assert.That(
                definition.ThrustNewtons,
                Is.EqualTo(expectedForce).Within(0.001f));
            Assert.That(definition.MassKg, Is.EqualTo(180f));
            Assert.That(
                NeoXThrusterPhysicsProfile.TryResolve(
                    sourceId,
                    out NeoXThrusterPhysicsProfile profile),
                Is.True);
            Assert.That(
                definition.ThrustNewtons,
                Is.EqualTo(profile.MaximumForce).Within(0.001f));
        }
        finally
        {
            foreach (GridModuleDefinition definition in model.Definitions.Values)
            {
                Object.DestroyImmediate(definition);
            }
        }
    }

    [Test]
    public void AirBuildModuleDetails_UseActualCpuMassAndThrusterForce()
    {
        var definition = ScriptableObject.CreateInstance<
            GridModuleDefinition>();
        try
        {
            definition.Configure(
                "neox@block:speed:speed_rocketsmall_112",
                "赛车小型喷射器",
                GridModuleCategory.MainThruster,
                null,
                new Vector3Int(1, 2, 1),
                180f,
                0f,
                0f,
                140f,
                44000f,
                null);
            var record = new ModularContentRecord
            {
                sourceId = "block:speed:speed_rocketsmall_112",
                neoXId = "speed_rocketsmall_112",
                behavior = "Thruster"
            };

            string details =
                AirBuildExperienceController.BuildModuleDetails(
                    record,
                    definition);

            Assert.That(details, Does.Contain("CPU 消耗  40"));
            Assert.That(details, Does.Contain("重量  180 kg"));
            Assert.That(details, Does.Contain("功能：推进器"));
            Assert.That(details, Does.Contain("推力：44 kN"));
        }
        finally
        {
            Object.DestroyImmediate(definition);
        }
    }

    [Test]
    public void AirBuildModuleDetails_UseCombatWeaponProfile()
    {
        var definition = ScriptableObject.CreateInstance<
            GridModuleDefinition>();
        try
        {
            definition.Configure(
                "neox@block:weapon:snipercannon_422",
                "狙击炮",
                GridModuleCategory.KineticWeapon,
                null,
                new Vector3Int(4, 2, 2),
                220f,
                0f,
                5f,
                140f,
                0f,
                null);
            var record = new ModularContentRecord
            {
                sourceId = "block:weapon:snipercannon_422",
                neoXId = "snipercannon_422",
                behavior = "SniperCannon"
            };

            string details =
                AirBuildExperienceController.BuildModuleDetails(
                    record,
                    definition);

            Assert.That(details, Does.Contain("CPU 消耗  180"));
            Assert.That(details, Does.Contain("功能：武器"));
            Assert.That(details, Does.Contain("伤害：240"));
        }
        finally
        {
            Object.DestroyImmediate(definition);
        }
    }

    [TestCase(
        "speed_rocketsmall_112",
        "NeoXExhaust_rocketsmall_boost.sfx",
        -1f)]
    [TestCase(
        "rocket_222",
        "NeoXExhaust_rocket_boost.sfx",
        1f)]
    [TestCase(
        "small_propeller_224",
        "NeoXExhaust_propeller_halo.sfx",
        1f)]
    public void ThrusterExhaustPresentation_UsesModelSpecificNozzleEnd(
        string neoXId,
        string effectName,
        float expectedDirectionZ)
    {
        var root = new GameObject("thruster-vfx-test");
        try
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.transform.SetParent(root.transform, false);
            NeoXBehaviorModule module =
                root.AddComponent<NeoXBehaviorModule>();
            module.Configure(new ModularContentRecord
            {
                sourceId = "block:test:" + neoXId,
                neoXId = neoXId,
                behavior = "Thruster",
                exhaustAxisLocal = new[] { 0f, 0f, 1f }
            });
            NeoXThrusterExhaustVfx vfx =
                root.AddComponent<NeoXThrusterExhaustVfx>();
            vfx.Configure(module);
            vfx.SetTargetThrottle(1f);
            vfx.SendMessage("LateUpdate");

            Transform effect = root.transform.Find(effectName);
            Assert.That(effect, Is.Not.Null);
            Assert.That(
                Vector3.Dot(effect.forward, Vector3.forward),
                Is.EqualTo(expectedDirectionZ).Within(0.001f));
            Assert.That(
                Mathf.Sign(effect.position.z),
                Is.EqualTo(expectedDirectionZ));
            Assert.That(
                Mathf.Abs(effect.position.z),
                Is.GreaterThan(0.5f));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ArcadeAllocator_MissingDriftAxisKeepsPrimaryIntent()
    {
        var allocator = new VirtualRcs24Allocator();
        allocator.Rebuild(
            new[]
            {
                Thruster(
                    0,
                    Vector3.zero,
                    Vector3.right,
                    100f,
                    0.01f)
            },
            Vector3.zero,
            false,
            0f,
            0f,
            0f);
        allocator.BeginStep(new[] { 1f });
        Rcs24SolveResult primary = allocator.SolveTranslation(
            new Rcs24SolveRequest
            {
                desired = Vector3.right * 60f,
                strictDirection = false,
                group = "test_primary_intent"
            });
        Rcs24SolveResult withMissingDrift = allocator.SolveTranslation(
            new Rcs24SolveRequest
            {
                desired = Vector3.right * 60f +
                          Vector3.back * 80f,
                strictDirection = false,
                group = "test_missing_drift"
            });

        Assert.That(primary.localForce.x, Is.EqualTo(60f).Within(0.01f));
        Assert.That(
            withMissingDrift.localForce.x,
            Is.EqualTo(primary.localForce.x).Within(0.01f));
        Assert.That(
            Mathf.Abs(withMissingDrift.localForce.z),
            Is.LessThan(0.01f));
        Assert.That(withMissingDrift.missingAxisMask & 4, Is.EqualTo(4));
    }

    [Test]
    public void ArcadeAssist_SpaceAndControlRequestOppositeVerticalSpeeds()
    {
        TrainingFlightAssistDemand ascend =
            TrainingFlightAssist.CalculateDemand(
                Vector3.zero,
                Vector3.zero,
                Vector3.zero,
                true,
                Vector3.up,
                14f);
        TrainingFlightAssistDemand descend =
            TrainingFlightAssist.CalculateDemand(
                Vector3.zero,
                Vector3.zero,
                Vector3.zero,
                true,
                Vector3.down,
                10f);

        Assert.That(ascend.targetVelocityWorld.y, Is.EqualTo(14f));
        Assert.That(ascend.controlAccelerationWorld.y, Is.GreaterThan(0f));
        Assert.That(descend.targetVelocityWorld.y, Is.EqualTo(-10f));
        Assert.That(descend.controlAccelerationWorld.y, Is.LessThan(0f));
    }

    [Test]
    public void ArcadeTargetSpeed_IncreasesWithInstalledDirectionalAuthority()
    {
        float weakForward =
            TrainingFlightAssist.CalculateTargetSpeed(2.5f, false);
        float strongForward =
            TrainingFlightAssist.CalculateTargetSpeed(8f, false);

        Assert.That(strongForward, Is.GreaterThan(weakForward));
        Assert.That(
            TrainingFlightAssist.CalculateTargetSpeed(8f, true),
            Is.GreaterThan(strongForward));
    }

    [Test]
    public void ArcadePlanarVectoringScalesWithLiveDriveAndStaysBounded()
    {
        const float mass = 500f;
        float noDrive = TrainingFlightAssist.CalculatePlanarAssistForce(
            mass,
            0f);
        float healthyDrive =
            TrainingFlightAssist.CalculatePlanarAssistForce(
                mass,
                6000f);
        float extremeDrive =
            TrainingFlightAssist.CalculatePlanarAssistForce(
                mass,
                1000000f);

        Assert.That(
            noDrive,
            Is.EqualTo(TrainingFlightAssist.BasePlanarAssistForce));
        Assert.That(healthyDrive, Is.GreaterThan(noDrive));
        Assert.That(
            extremeDrive,
            Is.EqualTo(
                TrainingFlightAssist.BasePlanarAssistForce +
                mass * TrainingFlightAssist.MaximumVectoringAcceleration)
                .Within(0.001f));
    }

    [Test]
    public void ArcadeCommandAuthorityKeepsWasdInReticleFrameDuringTurns()
    {
        Vector3 positive = new Vector3(4f, 6f, 12f);
        Vector3 negative = new Vector3(3f, 5f, 8f);

        float forward = TrainingFlightAssist.CalculateCommandAcceleration(
            Vector3.forward,
            positive,
            negative);
        float strafeLeft =
            TrainingFlightAssist.CalculateCommandAcceleration(
                Vector3.left,
                positive,
                negative);
        float diagonal = TrainingFlightAssist.CalculateCommandAcceleration(
            new Vector3(-1f, 0f, 1f),
            positive,
            negative);

        Assert.That(forward, Is.EqualTo(12f).Within(0.001f));
        Assert.That(strafeLeft, Is.EqualTo(3f).Within(0.001f));
        Assert.That(diagonal, Is.EqualTo(7.5f).Within(0.001f));
    }

    [Test]
    public void ArcadeAssist_ForwardVelocityFollowsThreeDimensionalAim()
    {
        Vector3 aim = new Vector3(0.35f, 0.60f, 0.72f).normalized;
        TrainingFlightAssistDemand demand =
            TrainingFlightAssist.CalculateDemand(
                Vector3.zero,
                Vector3.zero,
                Vector3.zero,
                true,
                aim,
                36f);

        Assert.That(
            Vector3.Dot(demand.targetVelocityWorld.normalized, aim),
            Is.GreaterThan(0.9999f));
        Assert.That(demand.targetVelocityWorld.y, Is.GreaterThan(0f));
        Assert.That(demand.targetSpeed, Is.EqualTo(36f).Within(0.001f));
    }

    [Test]
    public void ArcadeAssist_PrimaryVelocityResponseIsUnderTwoTenths()
    {
        Assert.That(
            TrainingFlightAssist.VelocityResponseSeconds,
            Is.LessThanOrEqualTo(0.20f));
    }

    [Test]
    public void TrainingAllocator_InstalledAndRemovedThrustersChangeRealAuthority()
    {
        var allocator = new VirtualRcs24Allocator();
        allocator.Rebuild(
            System.Array.Empty<Rcs24ThrusterInput>(),
            Vector3.zero,
            true,
            13000f,
            3000f,
            2500f);
        DirectionalAuthority24 coreOnly = allocator.Authority;

        allocator.Rebuild(
            new[]
            {
                Thruster(
                    0,
                    new Vector3(0f, 0f, -2f),
                    Vector3.forward,
                    6000f,
                    0.12f),
                Thruster(
                    1,
                    new Vector3(0f, -2f, 0f),
                    Vector3.up,
                    6000f,
                    0.12f)
            },
            Vector3.zero,
            true,
            13000f,
            3000f,
            2500f);
        DirectionalAuthority24 upgraded = allocator.Authority;

        Assert.That(
            upgraded.positiveForce.z - coreOnly.positiveForce.z,
            Is.EqualTo(6000f).Within(0.01f));
        Assert.That(
            upgraded.positiveForce.y - coreOnly.positiveForce.y,
            Is.EqualTo(6000f).Within(0.01f));

        allocator.Rebuild(
            System.Array.Empty<Rcs24ThrusterInput>(),
            Vector3.zero,
            true,
            13000f,
            3000f,
            2500f);
        Assert.That(
            allocator.Authority.positiveForce.z,
            Is.EqualTo(coreOnly.positiveForce.z).Within(0.01f));
        Assert.That(
            allocator.Authority.positiveForce.y,
            Is.EqualTo(coreOnly.positiveForce.y).Within(0.01f));
    }

    [Test]
    public void ArcadeTurnVectoringUsesDriveAuthorityButStandardDoesNot()
    {
        const float mass = 500f;
        const float driveForce = 6000f;
        float arcadePlanarForce =
            TrainingFlightAssist.CalculatePlanarAssistForce(
                mass,
                driveForce);
        Rcs24ThrusterInput[] forwardDrive =
        {
            Thruster(
                0,
                new Vector3(0f, 0f, -2f),
                Vector3.forward,
                driveForce,
                0.12f)
        };
        var allocator = new VirtualRcs24Allocator();
        allocator.Rebuild(
            forwardDrive,
            Vector3.zero,
            true,
            13000f,
            3000f,
            arcadePlanarForce);
        allocator.BeginStep(new[] { 1f });
        Rcs24SolveResult arcade = allocator.SolveTranslation(
            new Rcs24SolveRequest
            {
                desired = Vector3.right * arcadePlanarForce,
                strictDirection = true,
                group = "arcade_turn_vectoring"
            });

        Assert.That(arcade.commonScale, Is.GreaterThan(0.99f));
        Assert.That(
            arcade.localForce.x,
            Is.GreaterThan(TrainingFlightAssist.BasePlanarAssistForce));

        allocator.Rebuild(
            forwardDrive,
            Vector3.zero,
            false,
            13000f,
            3000f,
            arcadePlanarForce);
        allocator.BeginStep(new[] { 1f });
        Rcs24SolveResult standard = allocator.SolveTranslation(
            new Rcs24SolveRequest
            {
                desired = Vector3.right * arcadePlanarForce,
                strictDirection = true,
                group = "standard_no_vectoring"
            });

        Assert.That(standard.commonScale, Is.Zero);
        Assert.That(standard.missingAxisMask & 1, Is.EqualTo(1));
    }

    [Test]
    public void FlightKeyboardBinding_EitherControlKeyRequestsDescent()
    {
        Assert.That(
            KeyboardMouseFlightInput.ResolveVerticalAxis(false, true, false),
            Is.EqualTo(-1f));
        Assert.That(
            KeyboardMouseFlightInput.ResolveVerticalAxis(false, false, true),
            Is.EqualTo(-1f));
        Assert.That(
            KeyboardMouseFlightInput.ResolveVerticalAxis(true, true, false),
            Is.EqualTo(0f));
    }

    static Rcs24ThrusterInput Thruster(
        int index,
        Vector3 position,
        Vector3 direction,
        float force,
        float responseTime)
    {
        return new Rcs24ThrusterInput
        {
            sourceIndex = index,
            localPosition = position,
            localDirection = direction.normalized,
            maximumForce = force,
            responseTime = responseTime
        };
    }

    static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
        Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
        Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
    }

    static void SetPrivateField(
        object target,
        string fieldName,
        object value)
    {
        target.GetType()
            .GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)
            .SetValue(target, value);
    }

    static void AddExposedFace(
        VehiclePhysicsRc3State state,
        string runtimeId,
        Vector3 center,
        float area)
    {
        var faces = (System.Collections.Generic.List<ExposedAeroFace>)
            typeof(VehiclePhysicsRc3State)
                .GetField(
                    "faces",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                .GetValue(state);
        faces.Add(new ExposedAeroFace
        {
            runtimeId = runtimeId,
            centerLocal = center,
            normalLocal = Vector3.right,
            area = area,
            dragCoefficient = 0.7f
        });
    }

    static ModularContentRecord PaletteRecord(
        string id,
        string behavior)
    {
        return new ModularContentRecord
        {
            neoXId = id,
            behavior = behavior,
            selectableForAirBuild = true
        };
    }
}
