using System;
using System.Collections;
using System.Collections.Generic;
using SpacecraftEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityPlanet.ModularAssembly
{
    [DisallowMultipleComponent]
    public sealed class PlanetLabFlightEnvironmentController :
        MonoBehaviour,
        IGridFlightEnvironment,
        IGridFlightEnvironmentWarmup
    {
        const float MaximumSpawnSlope = 5f;
        const float MaximumSpawnRoughness = 0.75f;
        const float SpawnSearchRadius = 512f;
        const float SpawnSearchStep = 16f;
        const float WaterClearance = 1f;

        struct SpawnCandidate
        {
            public Vector3 position;
            public float maximumHeight;
            public float score;
            public bool strict;
        }

        InfinitePlanarSurfaceWorld world;
        GameObject worldRoot;
        PlanetEnvironmentProvider environmentProvider;
        bool environmentConfigured;
        PlanetPhysicalProfile physicalProfile;
        Rigidbody flightBody;
        RobocraftMotionCoordinator motionRc1;
        float spawnClearance = 1f;
        bool hasSpawnAddress;
        bool collisionSafetyHold;
        Vector3 collisionHoldPosition;
        Quaternion collisionHoldRotation;
        bool presentationCaptured;
        bool flightPresentationCaptured;
        Material buildSkybox;
        Material flightSkybox;
        bool buildFog;
        bool flightFog;
        AmbientMode buildAmbientMode;
        AmbientMode flightAmbientMode;
        Color buildAmbientLight;
        Color flightAmbientLight;
        CameraClearFlags buildClearFlags;
        CameraClearFlags flightClearFlags;
        readonly Dictionary<Renderer, bool> warmupRendererStates =
            new Dictionary<Renderer, bool>();
        bool suppressWarmupRendering;

        
        public int Priority => 0;
        public Quaternion PreparedRotation => Quaternion.identity;
public bool IsReady =>
            world != null && world.IsCenterCollisionReady;
        public PlanarSurfaceAddress SpawnAddress { get; private set; }

        public IEnumerator Warmup(Action<bool, string> completed)
        {
            suppressWarmupRendering = true;
            EnsureWorld();
            SuppressWarmupRenderers();
            yield return null;
            SuppressWarmupRenderers();
            ExitFlight();
            suppressWarmupRendering = false;
            RestoreWarmupRenderers();
            completed(true, "PlanetLab 中心地形已开始预热");
        }

        public void Initialize()
        {
        }

        public IEnumerator PrepareFlight(
            Rigidbody target,
            Action<bool, Vector3, string> completed)
        {
            if (target == null)
            {
                completed?.Invoke(false, Vector3.zero, "找不到模块飞船刚体。");
                yield break;
            }

            flightBody = target;
            motionRc1 = target.GetComponent<RobocraftMotionCoordinator>();
            if (target.GetComponent<PlanetFloatingOriginParticipant>() == null)
                target.gameObject.AddComponent<PlanetFloatingOriginParticipant>();

            CaptureBuildPresentation();
            string error = EnsureWorld();
            if (!string.IsNullOrEmpty(error))
            {
                completed?.Invoke(false, Vector3.zero, error);
                yield break;
            }

            ApplyFlightPresentation();
            suppressWarmupRendering = false;
            RestoreWarmupRenderers();
            worldRoot.SetActive(true);
            world.SetMovementTarget(target.transform);

            if (motionRc1 == null)
            {
                world.SetMovementTarget(null);
                worldRoot.SetActive(false);
                completed?.Invoke(
                    false,
                    Vector3.zero,
                    "RC3.2 motion coordinator is missing.");
                yield break;
            }
            EnsureEnvironmentProvider();
            PlanetEnvironmentRuntime.Active = environmentProvider;
            motionRc1.SetEnvironmentProvider(environmentProvider);
            GridTargetController targetController =
                FindObjectOfType<GridTargetController>();
            if (targetController != null)
                targetController.gameObject.SetActive(false);

            yield return null;
            float timeout = Time.realtimeSinceStartup + 20f;
            while (!world.IsCenterCollisionReady
                   && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }
            if (!world.IsCenterCollisionReady)
            {
                completed?.Invoke(
                    false,
                    Vector3.zero,
                    "PlanetLab中心地形碰撞生成超时。");
                yield break;
            }

            if (!TryResolveNaturalSpawn(target, out Vector3 spawn, out bool degraded))
            {
                completed?.Invoke(
                    false,
                    Vector3.zero,
                    "未找到可用的天然陆地出生点。");
                yield break;
            }

            target.position = spawn;
            target.rotation = Quaternion.identity;
            Physics.SyncTransforms();
            world.SetMovementTarget(target.transform);

            yield return null;
            timeout = Time.realtimeSinceStartup + 20f;
            while (!world.IsCenterCollisionReady
                   && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }
            if (!world.IsCenterCollisionReady)
            {
                completed?.Invoke(
                    false,
                    Vector3.zero,
                    "出生点地形碰撞生成超时。");
                yield break;
            }

            completed?.Invoke(
                true,
                spawn,
                degraded
                    ? "已进入无限试飞场；使用了最平整的天然陆地点。"
                    : "已进入PlanetLab无限自然试飞场。");
        }

        public IEnumerator ResetFlight(
            Rigidbody target,
            Action<bool, Vector3> completed)
        {
            if (target == null || world == null || !hasSpawnAddress)
            {
                completed?.Invoke(false, Vector3.zero);
                yield break;
            }

            Vector3 ground = world.FromPersistentAddress(SpawnAddress);
            if (world.Streamer != null
                && world.Streamer.TrySampleSurface(
                    SpawnAddress.x,
                    SpawnAddress.z,
                    out float sampledHeight,
                    out _))
            {
                ground.y = sampledHeight;
            }
            Vector3 spawn = ground + Vector3.up * spawnClearance;
            target.position = spawn;
            target.rotation = Quaternion.identity;
            if (!target.isKinematic)
            {
                target.velocity = Vector3.zero;
                target.angularVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();
            world.SetMovementTarget(target.transform);

            yield return null;
            float timeout = Time.realtimeSinceStartup + 20f;
            while (!world.IsCenterCollisionReady
                   && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }
            completed?.Invoke(world.IsCenterCollisionReady, spawn);
        }

        public void ExitFlight()
        {
            motionRc1?.ClearPlanetEnvironment();
            if (ReferenceEquals(
                    PlanetEnvironmentRuntime.Active,
                    environmentProvider))
            {
                PlanetEnvironmentRuntime.Active = null;
            }
            collisionSafetyHold = false;
            flightBody = null;
            motionRc1 = null;
            if (world != null)
                world.SetMovementTarget(null);
            if (worldRoot != null)
                worldRoot.SetActive(false);
            RestoreBuildPresentation();
        }

        void FixedUpdate()
        {
            if (flightBody == null
                || worldRoot == null
                || !worldRoot.activeInHierarchy)
            {
                return;
            }

            if (!world.IsCenterCollisionReady)
            {
                if (!flightBody.isKinematic)
                {
                    collisionHoldPosition = flightBody.position;
                    collisionHoldRotation = flightBody.rotation;
                    flightBody.velocity = Vector3.zero;
                    flightBody.angularVelocity = Vector3.zero;
                    flightBody.isKinematic = true;
                    collisionSafetyHold = true;
                }
                return;
            }

            float groundHeight = world.Streamer != null
                ? world.Streamer.SampleHeight(
                    flightBody.position.x,
                    flightBody.position.z)
                : 0f;
            if (collisionSafetyHold)
            {
                collisionHoldPosition.y = Mathf.Max(
                    collisionHoldPosition.y,
                    groundHeight + spawnClearance);
                flightBody.position = collisionHoldPosition;
                flightBody.rotation = collisionHoldRotation;
                Physics.SyncTransforms();
                flightBody.isKinematic = false;
                flightBody.velocity = Vector3.zero;
                flightBody.angularVelocity = Vector3.zero;
                flightBody.WakeUp();
                collisionSafetyHold = false;
            }
            EnsureEnvironmentProvider();
            PlanetEnvironmentSample sample = environmentProvider.Sample(
                flightBody.worldCenterOfMass,
                Time.fixedTimeAsDouble);
            motionRc1?.SetEnvironmentProvider(environmentProvider);
        }

        void LateUpdate()
        {
            if (suppressWarmupRendering)
                SuppressWarmupRenderers();
        }

        void SuppressWarmupRenderers()
        {
            if (worldRoot == null)
                return;
            foreach (Renderer renderer in
                     worldRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;
                if (!warmupRendererStates.ContainsKey(renderer))
                    warmupRendererStates.Add(renderer, renderer.enabled);
                else if (renderer.enabled)
                    warmupRendererStates[renderer] = true;
                renderer.enabled = false;
            }
        }

        void RestoreWarmupRenderers()
        {
            foreach (KeyValuePair<Renderer, bool> pair in
                     warmupRendererStates)
            {
                if (pair.Key != null)
                    pair.Key.enabled = pair.Value;
            }
            warmupRendererStates.Clear();
        }

        string EnsureWorld()
        {
            if (world != null)
                return string.Empty;

            ProceduralPlanetPreset preset = CreateTemperateOceanPreset();
            GalaxyPlanetDefinition definition = preset.CloneDefinition();
            Destroy(preset);
            physicalProfile = definition.celestial != null
                ? definition.celestial.physical?.Clone()
                : PlanetPhysicalProfile.CreateEarthLike();

            try
            {
                worldRoot = new GameObject("PlanetLabFlightWorld");
                world = worldRoot.AddComponent<InfinitePlanarSurfaceWorld>();
                world.Configure(
                    definition,
                    null,
                    null,
                    null,
                    new List<PlanetSurfacePropSpawnSettings>());
                EnsureEnvironmentProvider();
                Material customSkybox = Resources.Load<Material>(
                    "Skyboxes/BloubergSunrise/Blouberg Sunrise Equirect");
                if (customSkybox != null)
                    RenderSettings.skybox = customSkybox;
                CaptureFlightPresentation();
                return string.Empty;
            }
            catch (Exception exception)
            {
                if (worldRoot != null)
                    Destroy(worldRoot);
                worldRoot = null;
                world = null;
                RestoreBuildPresentation();
                return "PlanetLab无限地形初始化失败：" + exception.Message;
            }
        }

        void EnsureEnvironmentProvider()
        {
            if (worldRoot == null || world == null)
                return;
            PlanetEnvironmentProvider resolved =
                environmentProvider != null
                ? environmentProvider
                : worldRoot.GetComponent<PlanetEnvironmentProvider>()
                  ?? worldRoot.AddComponent<PlanetEnvironmentProvider>();
            if (ReferenceEquals(environmentProvider, resolved) &&
                environmentConfigured)
            {
                return;
            }
            environmentProvider = resolved;
            environmentProvider.Configure(
                world,
                physicalProfile ?? PlanetPhysicalProfile.CreateEarthLike());
            environmentConfigured = true;
        }

        void Update()
        {
            if (environmentProvider != null &&
                worldRoot != null &&
                worldRoot.activeInHierarchy &&
                Input.GetKeyDown(KeyCode.F8))
            {
                environmentProvider.ForceNoWind =
                    !environmentProvider.ForceNoWind;
            }
        }

        void OnGUI()
        {
            if (environmentProvider == null ||
                worldRoot == null ||
                !worldRoot.activeInHierarchy)
            {
                return;
            }
            string label = environmentProvider.ForceNoWind
                ? "Wind: Calm baseline (F8)"
                : "Wind: Planet profile (F8)";
            Rect rect = new Rect(
                Mathf.Max(8f, Screen.width - 228f),
                Mathf.Max(8f, Screen.height - 52f),
                212f,
                36f);
            if (GUI.Button(rect, label))
                environmentProvider.ForceNoWind =
                    !environmentProvider.ForceNoWind;
        }

        bool TryResolveNaturalSpawn(
            Rigidbody target,
            out Vector3 spawn,
            out bool degraded)
        {
            spawn = Vector3.zero;
            degraded = false;
            if (world == null || world.Streamer == null)
                return false;

            Bounds shipBounds = ResolveShipBounds(target);
            float halfX = Mathf.Max(1f, shipBounds.extents.x + 1f);
            float halfZ = Mathf.Max(1f, shipBounds.extents.z + 1f);
            spawnClearance = Mathf.Max(
                0.5f,
                target.position.y - shipBounds.min.y + 0.25f);

            bool foundStrict = false;
            bool foundFallback = false;
            SpawnCandidate bestStrict = default;
            SpawnCandidate bestFallback = default;
            for (float radius = 0f;
                 radius <= SpawnSearchRadius;
                 radius += SpawnSearchStep)
            {
                int samples = radius < 0.01f
                    ? 1
                    : Mathf.Clamp(
                        Mathf.CeilToInt(Mathf.PI * 2f * radius / 32f),
                        8,
                        64);
                for (int index = 0; index < samples; index++)
                {
                    float angle = samples == 1
                        ? 0f
                        : index * Mathf.PI * 2f / samples;
                    Vector2 center = new Vector2(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius);
                    if (!TryEvaluateCandidate(
                        center,
                        halfX,
                        halfZ,
                        out SpawnCandidate candidate))
                    {
                        continue;
                    }

                    if (candidate.strict
                        && (!foundStrict
                            || candidate.score < bestStrict.score))
                    {
                        bestStrict = candidate;
                        foundStrict = true;
                    }
                    if (!foundFallback
                        || candidate.score < bestFallback.score)
                    {
                        bestFallback = candidate;
                        foundFallback = true;
                    }
                }
            }

            if (!foundStrict && !foundFallback)
                return false;
            SpawnCandidate selected =
                foundStrict ? bestStrict : bestFallback;
            degraded = !foundStrict;
            spawn = new Vector3(
                selected.position.x,
                selected.maximumHeight + spawnClearance,
                selected.position.z);
            SpawnAddress = world.ToPersistentAddress(new Vector3(
                selected.position.x,
                selected.maximumHeight,
                selected.position.z));
            hasSpawnAddress = true;
            return true;
        }

        bool TryEvaluateCandidate(
            Vector2 center,
            float halfX,
            float halfZ,
            out SpawnCandidate candidate)
        {
            candidate = default;
            Vector2[] offsets =
            {
                Vector2.zero,
                new Vector2(-halfX, -halfZ),
                new Vector2(-halfX, halfZ),
                new Vector2(halfX, -halfZ),
                new Vector2(halfX, halfZ),
                new Vector2(-halfX, 0f),
                new Vector2(halfX, 0f),
                new Vector2(0f, -halfZ),
                new Vector2(0f, halfZ)
            };
            float minimumHeight = float.PositiveInfinity;
            float maximumHeight = float.NegativeInfinity;
            float maximumSlope = 0f;
            for (int index = 0; index < offsets.Length; index++)
            {
                Vector3 localPoint = new Vector3(
                    center.x + offsets[index].x,
                    0f,
                    center.y + offsets[index].y);
                PlanarSurfaceAddress address =
                    world.ToPersistentAddress(localPoint);
                if (!world.Streamer.TrySampleSurface(
                    address.x,
                    address.z,
                    out float height,
                    out Vector3 normal)
                    || height <= world.SeaHeight + WaterClearance)
                {
                    return false;
                }

                minimumHeight = Mathf.Min(minimumHeight, height);
                maximumHeight = Mathf.Max(maximumHeight, height);
                maximumSlope = Mathf.Max(
                    maximumSlope,
                    Vector3.Angle(normal, Vector3.up));
            }

            float roughness = maximumHeight - minimumHeight;
            candidate = new SpawnCandidate
            {
                position = new Vector3(center.x, 0f, center.y),
                maximumHeight = maximumHeight,
                strict = maximumSlope <= MaximumSpawnSlope
                    && roughness <= MaximumSpawnRoughness,
                score = roughness * 20f
                    + maximumSlope
                    + center.magnitude * 0.002f
            };
            return true;
        }

        static Bounds ResolveShipBounds(Rigidbody target)
        {
            Collider[] colliders =
                target.GetComponentsInChildren<Collider>(true);
            bool initialized = false;
            Bounds bounds = new Bounds(target.position, Vector3.one);
            for (int index = 0; index < colliders.Length; index++)
            {
                Collider collider = colliders[index];
                if (collider == null
                    || !collider.enabled
                    || collider.isTrigger)
                {
                    continue;
                }
                if (!initialized)
                {
                    bounds = collider.bounds;
                    initialized = true;
                }
                else
                    bounds.Encapsulate(collider.bounds);
            }
            ModularWheelRuntime[] wheels =
                target.GetComponentsInChildren<ModularWheelRuntime>(false);
            for (int index = 0; index < wheels.Length; index++)
            {
                ModularWheelRuntime wheel = wheels[index];
                if (wheel == null ||
                    !wheel.enabled ||
                    !wheel.gameObject.activeInHierarchy)
                    continue;
                float extent = Mathf.Max(
                    0.12f,
                    wheel.Profile.radius + wheel.Profile.suspensionTravel);
                Bounds wheelBounds = new Bounds(
                    wheel.transform.position,
                    Vector3.one * extent * 2f);
                if (!initialized)
                {
                    bounds = wheelBounds;
                    initialized = true;
                }
                else
                    bounds.Encapsulate(wheelBounds);
            }
            return bounds;
        }

        void CaptureBuildPresentation()
        {
            if (presentationCaptured)
                return;
            presentationCaptured = true;
            buildSkybox = RenderSettings.skybox;
            buildFog = RenderSettings.fog;
            buildAmbientMode = RenderSettings.ambientMode;
            buildAmbientLight = RenderSettings.ambientLight;
            Camera camera = Camera.main;
            buildClearFlags = camera != null
                ? camera.clearFlags
                : CameraClearFlags.SolidColor;
        }

        void CaptureFlightPresentation()
        {
            flightPresentationCaptured = true;
            flightSkybox = RenderSettings.skybox;
            flightFog = RenderSettings.fog;
            flightAmbientMode = RenderSettings.ambientMode;
            flightAmbientLight = RenderSettings.ambientLight;
            Camera camera = Camera.main;
            flightClearFlags = camera != null
                ? camera.clearFlags
                : CameraClearFlags.Skybox;
        }

        void ApplyFlightPresentation()
        {
            if (!flightPresentationCaptured)
                return;
            RenderSettings.skybox = flightSkybox;
            RenderSettings.fog = flightFog;
            RenderSettings.ambientMode = flightAmbientMode;
            RenderSettings.ambientLight = flightAmbientLight;
            Camera camera = Camera.main;
            if (camera != null)
                camera.clearFlags = flightClearFlags;
        }

        void RestoreBuildPresentation()
        {
            if (!presentationCaptured)
                return;
            RenderSettings.skybox = buildSkybox;
            RenderSettings.fog = buildFog;
            RenderSettings.ambientMode = buildAmbientMode;
            RenderSettings.ambientLight = buildAmbientLight;
            Camera camera = Camera.main;
            if (camera != null)
                camera.clearFlags = buildClearFlags;
        }

        static ProceduralPlanetPreset CreateTemperateOceanPreset()
        {
            ProceduralPlanetPreset preset =
                ScriptableObject.CreateInstance<ProceduralPlanetPreset>();
            preset.ApplyTemplate(
                ProceduralPlanetLabTemplate.TemperateOcean);
            preset.seed = 7319;
            preset.radius = 817f;
            preset.maximumTerrainElevation = 45f;
            preset.terrain.continentScale = 0.018f;
            preset.terrain.continentHeight = 11f;
            preset.terrain.detailScale = 0.065f;
            preset.terrain.detailHeight = 3.6f;
            preset.terrain.ridgeHeight = 7f;
            preset.terrain.continentThreshold = 0.5769f;
            preset.terrain.continentWarp = 0.339f;
            preset.terrain.continentSharpness = 1.25f;
            preset.terrain.mountainMask = 1f;
            preset.terrain.oceanFloorDepth = 0.58f;
            preset.terrain.terraceStrength = 0f;
            preset.visual.lowlandColor =
                new Color(0.12f, 0.42f, 0.12f, 1f);
            preset.visual.highlandColor =
                new Color(0.45f, 0.68f, 0.2f, 1f);
            preset.visual.cliffColor =
                new Color(0.2f, 0.16f, 0.1f, 1f);
            preset.visual.rockColor =
                new Color(0.28f, 0.25f, 0.18f, 1f);
            preset.visual.accentColor =
                new Color(0.6f, 0.82f, 0.22f, 1f);
            preset.visual.oceanEnabled = true;
            preset.visual.deepOceanColor =
                new Color(0.01f, 0.09f, 0.2f, 1f);
            preset.visual.shallowOceanColor =
                new Color(0.02f, 0.48f, 0.62f, 1f);
            preset.visual.shoreColor =
                new Color(0.78f, 0.68f, 0.38f, 1f);
            preset.visual.snowColor =
                new Color(0.94f, 0.95f, 0.86f, 1f);
            preset.visual.oceanLevel = 0.005f;
            preset.visual.shoreWidth = 0.0797f;
            preset.visual.snowLine = 0.72f;
            preset.visual.snowAmount = 0.65f;
            preset.visual.oceanSmoothness = 0.86f;
            preset.visual.oceanWaveStrength = 0.32f;
            preset.visual.oceanFoamStrength = 0.902f;
            preset.visual.horizonColor =
                new Color(0.42f, 0.68f, 0.9f, 1f);
            preset.visual.zenithColor =
                new Color(0.08f, 0.22f, 0.42f, 1f);
            preset.visual.groundAmbientColor =
                new Color(0.08f, 0.09f, 0.08f, 1f);
            preset.visual.atmosphereThickness = 0.65f;
            preset.visual.cloudCoverage = 0.35f;
            preset.visual.hazeStrength = 0.22f;
            preset.planar.autoAnchor = true;
            preset.planar.anchorLatitude = 28.079948f;
            preset.planar.anchorLongitude = -156.45245f;
            return preset;
        }
    }
}
