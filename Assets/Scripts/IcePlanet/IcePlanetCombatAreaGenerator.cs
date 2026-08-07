using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.IcePlanet
{
    [DefaultExecutionOrder(-180)]
    [DisallowMultipleComponent]
    public sealed class IcePlanetCombatAreaGenerator : MonoBehaviour
    {
        const string SurfaceSceneName = "star";
        InfinitePlanarSurfaceWorld world;
        IcePlanetCombatArea area;
        bool usingFrozenShelf;
        float frozenShelfHeight;

        public bool IsGenerated { get; private set; }
        public string LastError { get; private set; }
        public IcePlanetCombatArea Area => area;

        IEnumerator Start()
        {
            if (!string.Equals(
                    gameObject.scene.name,
                    SurfaceSceneName,
                    System.StringComparison.Ordinal))
            {
                yield break;
            }
            if (!PlanetOrbitChapterSelectionContext.HasSelection)
            {
                yield break;
            }
            // Urban missions (including every modular Boss encounter) own
            // their complete combat surface. Starting the tundra generator
            // as well caused its anchors to probe an urban-only surface,
            // flooding the Console and risking two PCG layers in one battle.
            if (PlanetOrbitChapterSelectionContext.EnvironmentKind ==
                PlanetMissionEnvironmentKind.Urban)
            {
                yield break;
            }

            int remainingFrames = 1800;
            while (remainingFrames-- > 0)
            {
                world = FindObjectOfType<InfinitePlanarSurfaceWorld>();
                if (world != null
                    && world.IsCenterCollisionReady
                    && world.IsInitialPlayerPlaced)
                {
                    break;
                }
                yield return null;
            }
            if (world == null
                || !world.IsCenterCollisionReady
                || !world.IsInitialPlayerPlaced)
            {
                Fail("Infinite planar terrain did not become ready for the combat area.");
                yield break;
            }
            if (world.Definition == null
                || world.Definition.climate != PlanetClimate.Tundra)
            {
                Debug.Log(
                    "[IcePlanet] A planet mission was selected, but the current "
                    + "planet is not Tundra. Winter combat PCG was intentionally "
                    + "left inactive so biome content cannot leak onto other planets.",
                    this);
                yield break;
            }

            IcePlanetCombatLayoutPlan plan =
                IcePlanetCombatLayoutPlanner.Create(
                    PlanetOrbitChapterSelectionContext.MissionId,
                    PlanetOrbitChapterSelectionContext.MissionSeed);
            List<string> errors =
                IcePlanetCombatLayoutValidator.Validate(plan);
            if (errors.Count > 0)
            {
                Fail("Layout validation failed: "
                    + string.Join(" | ", errors));
                yield break;
            }

            Vector3 forward = ResolveApproachDirection(world.Player);
            Vector3 requestedCenter = world.Player.transform.position
                + forward * plan.ApproachDistance;
            if (!TryFindCombatAreaCenter(
                    plan,
                    requestedCenter,
                    forward,
                    out PlanetSurfaceSample centerSample,
                    out string centerSearchReport))
            {
                Fail(
                    "No usable combat-area center was found. "
                    + centerSearchReport);
                yield break;
            }

            GameObject root = new GameObject(
                "IcePlanetCombatArea_" + plan.MissionId);
            SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            root.transform.SetParent(world.transform, true);
            root.transform.SetPositionAndRotation(
                centerSample.point,
                Quaternion.LookRotation(forward, Vector3.up));
            root.AddComponent<PlanetFloatingOriginParticipant>();
            area = root.AddComponent<IcePlanetCombatArea>();
            area.Configure(
                world.Definition.planetId,
                plan,
                centerSample.point);
            if (usingFrozenShelf)
                CreateFrozenShelf(plan);

            for (int index = 0; index < plan.Anchors.Count; index++)
            {
                CreateAnchor(plan.Anchors[index]);
            }

            PlanetEnvironmentProvider baseEnvironment =
                FindObjectOfType<PlanetEnvironmentProvider>(true);
            IcePlanetEnvironmentConditions conditions =
                root.AddComponent<IcePlanetEnvironmentConditions>();
            conditions.Configure(
                baseEnvironment,
                area,
                plan.MissionId,
                plan.Seed);
            IsGenerated = true;
            Debug.Log(
                "[IcePlanet] Generated mission='" + plan.MissionId
                + "' seed=" + plan.Seed
                + " signature=" + plan.CalculateSignature().ToString("X8")
                + " decorations=0 (disabled)"
                + " anchors=" + plan.Anchors.Count
                + " collisionFoundation=" + usingFrozenShelf
                + ". Existing spacecraft physics ownership was preserved.",
                area);
        }

        bool TryFindCombatAreaCenter(
            IcePlanetCombatLayoutPlan plan,
            Vector3 requestedCenter,
            Vector3 forward,
            out PlanetSurfaceSample result,
            out string report)
        {
            result = default;
            report = string.Empty;
            usingFrozenShelf = false;
            frozenShelfHeight = 0f;

            Vector3 playerPosition = world.Player.transform.position;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.right;

            var candidates = new List<Vector3>(360);
            float[] approachFractions = { 1f, 0.8f, 0.6f, 0.4f, 0.2f, 0f };
            float[] lateralOffsets = { 0f, -32f, 32f, -64f, 64f };
            for (int distanceIndex = 0;
                 distanceIndex < approachFractions.Length;
                 distanceIndex++)
            {
                Vector3 along = playerPosition
                    + forward
                    * (plan.ApproachDistance
                       * approachFractions[distanceIndex]);
                for (int lateralIndex = 0;
                     lateralIndex < lateralOffsets.Length;
                     lateralIndex++)
                {
                    candidates.Add(
                        along + right * lateralOffsets[lateralIndex]);
                }
            }

            const float ringStep = 24f;
            const int ringCount = 18;
            const int samplesPerRing = 16;
            for (int ring = 1; ring <= ringCount; ring++)
            {
                float radius = ring * ringStep;
                for (int index = 0; index < samplesPerRing; index++)
                {
                    float angle = index * Mathf.PI * 2f / samplesPerRing;
                    candidates.Add(
                        requestedCenter
                        + right * (Mathf.Cos(angle) * radius)
                        + forward * (Mathf.Sin(angle) * radius));
                }
            }

            PlanetSurfaceSample bestLevel = default;
            PlanetSurfaceSample bestModerate = default;
            PlanetSurfaceSample bestEmergency = default;
            PlanetSurfaceSample bestWater = default;
            bool hasLevel = false;
            bool hasModerate = false;
            bool hasEmergency = false;
            bool hasWater = false;
            float bestLevelScore = float.PositiveInfinity;
            float bestModerateScore = float.PositiveInfinity;
            float bestEmergencyScore = float.PositiveInfinity;
            float bestWaterScore = float.PositiveInfinity;
            float minimumDrySlope = float.PositiveInfinity;
            int projectedCount = 0;
            int dryCount = 0;
            int waterCount = 0;
            int outsideFiniteAreaCount = 0;
            float arenaClearance = plan.ArenaSize.magnitude * 0.5f + 12f;

            for (int index = 0; index < candidates.Count; index++)
            {
                Vector3 candidate = candidates[index];
                if (!world.ContainsFiniteCombatPoint(
                        candidate,
                        arenaClearance))
                {
                    outsideFiniteAreaCount++;
                    continue;
                }
                if (!world.TryProjectToSurface(
                        candidate,
                        out PlanetSurfaceSample sample))
                {
                    continue;
                }
                projectedCount++;
                float planarDistance = Vector2.Distance(
                    new Vector2(candidate.x, candidate.z),
                    new Vector2(requestedCenter.x, requestedCenter.z));
                float slope = Vector3.Angle(sample.normal, Vector3.up);
                if (sample.isWater)
                {
                    waterCount++;
                    if (planarDistance < bestWaterScore)
                    {
                        bestWaterScore = planarDistance;
                        bestWater = sample;
                        hasWater = true;
                    }
                    continue;
                }

                dryCount++;
                minimumDrySlope = Mathf.Min(minimumDrySlope, slope);
                float score = planarDistance + slope * 0.4f;
                if (slope <= 30f && score < bestLevelScore)
                {
                    bestLevelScore = score;
                    bestLevel = sample;
                    hasLevel = true;
                }
                if (slope <= 38f && score < bestModerateScore)
                {
                    bestModerateScore = score;
                    bestModerate = sample;
                    hasModerate = true;
                }
                if (slope <= 48f && score < bestEmergencyScore)
                {
                    bestEmergencyScore = score;
                    bestEmergency = sample;
                    hasEmergency = true;
                }
            }

            report = "candidates=" + candidates.Count
                + ", projected=" + projectedCount
                + ", dry=" + dryCount
                + ", water=" + waterCount
                + ", outsideFiniteArea=" + outsideFiniteAreaCount
                + ", minimumDrySlope="
                + (float.IsPositiveInfinity(minimumDrySlope)
                    ? "n/a"
                    : minimumDrySlope.ToString("F1"));
            if (hasLevel)
            {
                result = bestLevel;
                return true;
            }
            if (hasModerate)
            {
                result = bestModerate;
                Debug.LogWarning(
                    "[IcePlanet] No center at or below 30 degrees was found; "
                    + "using a moderate-slope fallback. " + report,
                    this);
                return true;
            }
            if (hasEmergency)
            {
                result = bestEmergency;
                Debug.LogWarning(
                    "[IcePlanet] No center at or below 38 degrees was found; "
                    + "using a player-accessible terrain fallback. " + report,
                    this);
                return true;
            }
            if (hasWater && world.OceanEnabled)
            {
                PlanarSurfaceAddress address =
                    world.ToPersistentAddress(bestWater.point);
                frozenShelfHeight = world.SeaHeight + 0.18f;
                result = new PlanetSurfaceSample
                {
                    point = world.FromPersistentAddress(
                        new PlanarSurfaceAddress(
                            address.x,
                            address.z,
                            frozenShelfHeight)),
                    normal = Vector3.up,
                    height = frozenShelfHeight,
                    isWater = false
                };
                usingFrozenShelf = true;
                Debug.LogWarning(
                    "[IcePlanet] No dry center was available, so the Tundra "
                    + "mission will use a load-bearing frozen shelf above sea "
                    + "level. " + report,
                    this);
                return true;
            }
            return false;
        }

        void CreateFrozenShelf(IcePlanetCombatLayoutPlan plan)
        {
            var shelf = new GameObject("PCG_CombatFoundation_CollisionOnly");
            shelf.transform.SetParent(area.transform, false);
            shelf.transform.localPosition = new Vector3(0f, -0.4f, 0f);
            shelf.transform.localRotation = Quaternion.identity;
            BoxCollider collider = shelf.AddComponent<BoxCollider>();
            collider.size = new Vector3(
                plan.ArenaSize.x + 72f,
                0.8f,
                plan.ArenaSize.y + 72f);
        }

        void CreateAnchor(IcePlanetCombatLayoutAnchor definition)
        {
            Vector3 candidate = area.transform.TransformPoint(
                new Vector3(
                    definition.position.x,
                    0f,
                    definition.position.y));
            if (!TryResolveSurfacePoint(
                    candidate,
                    45f,
                    out PlanetSurfaceSample surface))
            {
                Debug.LogWarning(
                    "[IcePlanet] Anchor '" + definition.stableId
                    + "' could not be projected to terrain.",
                    area);
                return;
            }
            GameObject value = new GameObject(
                "Anchor_" + definition.role + "_" + definition.stableId);
            value.transform.SetParent(area.transform, true);
            value.transform.SetPositionAndRotation(
                surface.point,
                area.transform.rotation
                    * Quaternion.Euler(0f, definition.yawDegrees, 0f));
            IcePlanetCombatAnchor anchor =
                value.AddComponent<IcePlanetCombatAnchor>();
            anchor.Configure(
                definition.stableId,
                definition.role,
                definition.groupIndex);
            area.RegisterAnchor(anchor);

            if (definition.role == IcePlanetCombatAnchorRole.Objective)
            {
                SphereCollider trigger = value.AddComponent<SphereCollider>();
                trigger.radius = 5f;
                trigger.isTrigger = true;
            }
            else if (definition.role == IcePlanetCombatAnchorRole.Extraction)
            {
                SphereCollider trigger = value.AddComponent<SphereCollider>();
                trigger.radius = 7f;
                trigger.isTrigger = true;
            }
        }

        bool TryResolveSurfacePoint(
            Vector3 candidate,
            float maximumSlope,
            out PlanetSurfaceSample sample)
        {
            if (usingFrozenShelf && area != null)
            {
                Vector3 local = area.transform.InverseTransformPoint(candidate);
                Vector2 half = area.ArenaSize * 0.5f + Vector2.one * 36f;
                if (Mathf.Abs(local.x) <= half.x
                    && Mathf.Abs(local.z) <= half.y)
                {
                    PlanarSurfaceAddress address =
                        world.ToPersistentAddress(candidate);
                    sample = new PlanetSurfaceSample
                    {
                        point = world.FromPersistentAddress(
                            new PlanarSurfaceAddress(
                                address.x,
                                address.z,
                                frozenShelfHeight)),
                        normal = Vector3.up,
                        height = frozenShelfHeight,
                        isWater = false
                    };
                    return true;
                }
            }
            if (world.TryProjectToSurface(candidate, out sample)
                && !sample.isWater
                && Vector3.Angle(sample.normal, Vector3.up) <= maximumSlope)
            {
                return true;
            }
            sample = default;
            return false;
        }

        static Vector3 ResolveApproachDirection(
            VoxelPlanetPlayerController player)
        {
            Vector3 forward = player != null
                ? Vector3.ProjectOnPlane(player.transform.forward, Vector3.up)
                : Vector3.forward;
            return forward.sqrMagnitude > 0.001f
                ? forward.normalized
                : Vector3.forward;
        }

        void Fail(string message)
        {
            LastError = message ?? "Unknown ice-planet PCG error.";
            Debug.LogError("[IcePlanet] " + LastError, this);
        }

    }

    internal static class IcePlanetCombatAreaRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            HandleSceneLoaded(
                SceneManager.GetActiveScene(),
                LoadSceneMode.Single);
        }

        static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!scene.IsValid()
                || !string.Equals(
                    scene.name,
                    "star",
                    System.StringComparison.Ordinal)
                || !PlanetOrbitChapterSelectionContext.HasSelection)
            {
                return;
            }
            IcePlanetCombatAreaGenerator[] existing =
                Object.FindObjectsOfType<IcePlanetCombatAreaGenerator>(true);
            for (int index = 0; index < existing.Length; index++)
            {
                if (existing[index] != null
                    && existing[index].gameObject.scene == scene)
                {
                    return;
                }
            }
            GameObject host = new GameObject("IcePlanetCombatPCG");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<IcePlanetCombatAreaGenerator>();
        }
    }
}
