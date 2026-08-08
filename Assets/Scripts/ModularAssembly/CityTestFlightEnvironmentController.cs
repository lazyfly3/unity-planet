using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityPlanet.CityPcg;

namespace UnityPlanet.ModularAssembly
{
    /// <summary>
    /// Runtime adapter that exposes the formal combat-city PCG as a selectable
    /// ModularAssemblyLab test-flight map.  It owns only map presentation,
    /// collision and spawn points; ship forces stay in RobocraftMotionRC1.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityTestFlightEnvironmentController :
        MonoBehaviour,
        IGridFlightEnvironment,
        IGridFlightEnvironmentWarmup,
        ICombatArenaProvider
    {
        const string TemplateResourcePath =
            "PlanetSurface/UrbanCombatCityTemplate";
        const int TestCitySeed = 7319;

        readonly Dictionary<Collider, bool> buildColliderStates =
            new Dictionary<Collider, bool>();
        readonly Dictionary<Renderer, bool> buildRendererStates =
            new Dictionary<Renderer, bool>();

        GameObject cityRoot;
        AirCombatCityPcgLab cityLab;
        Rigidbody flightBody;
        Vector3 preparedPosition;
        Quaternion preparedRotation = Quaternion.identity;
        bool ready;
        string failure = string.Empty;
        CombatTestMode mode = CombatTestMode.Duel;

        public int Priority => 300;
        public Quaternion PreparedRotation => preparedRotation;
        public CombatTestMode Mode => mode;
        public Vector3 BattleCenter => cityLab != null && cityLab.Plan != null
            ? cityLab.Plan.objective
            : Vector3.zero;
        public float WarningRadius => cityLab != null
            ? cityLab.Settings.mapSize * 0.42f
            : 680f;
        public float ForfeitRadius => cityLab != null
            ? cityLab.Settings.mapSize * 0.49f
            : 790f;
        public float FlightCeiling => cityLab != null
            ? cityLab.Settings.maximumAltitude
            : 350f;
        public bool IsReady => ready;
        public string Failure => failure;

        public IEnumerator Warmup(Action<bool, string> completed)
        {
            if (!ready && string.IsNullOrEmpty(failure))
            {
                // Let the UI render its loading state before the bounded city
                // planning/build pass runs on the Unity main thread.
                yield return null;
                EnsureCity();
                yield return null;
            }
            completed?.Invoke(
                ready,
                ready
                    ? "城市试飞场已生成，可直接起飞。"
                    : failure);
        }

        public IEnumerator PrepareFlight(
            Rigidbody target,
            Action<bool, Vector3, string> completed)
        {
            if (!ready)
                EnsureCity();
            if (!ready || target == null)
            {
                completed?.Invoke(
                    false,
                    target != null ? target.position : Vector3.zero,
                    string.IsNullOrEmpty(failure)
                        ? "城市试飞场不可用。"
                        : failure);
                yield break;
            }

            flightBody = target;
            DisableBuildPresentation(target);
            cityRoot.SetActive(true);
            ResolveSpawn(out preparedPosition, out preparedRotation);
            target.position = preparedPosition;
            target.rotation = preparedRotation;
            Physics.SyncTransforms();
            yield return null;
            completed?.Invoke(
                true,
                preparedPosition,
                "已进入城市试飞场；F6 可实时调节街机飞行手感。");
        }

        public IEnumerator ResetFlight(
            Rigidbody target,
            Action<bool, Vector3> completed)
        {
            if (!ready || target == null)
            {
                completed?.Invoke(false, Vector3.zero);
                yield break;
            }
            target.position = preparedPosition;
            target.rotation = preparedRotation;
            if (!target.isKinematic)
            {
                target.velocity = Vector3.zero;
                target.angularVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();
            yield return null;
            completed?.Invoke(true, preparedPosition);
        }

        public void ExitFlight()
        {
            flightBody = null;
            if (cityRoot != null)
                cityRoot.SetActive(false);
            RestoreBuildPresentation();
        }

        public void SetMode(CombatTestMode value)
        {
            mode = value;
        }

        public bool TryGetPlayerSpawn(
            out Vector3 position,
            out Quaternion rotation)
        {
            if (!ready)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return false;
            }
            ResolveSpawn(out position, out rotation);
            return true;
        }

        public bool TryGetEnemySpawn(
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            AirCombatFlightRoute main = FindMainRoute();
            if (!ready || main == null || main.points == null ||
                main.points.Length < 2)
            {
                return false;
            }
            int index = Mathf.Max(1, main.points.Length - 2);
            position = main.points[index];
            Vector3 forward = BattleCenter - position;
            forward.y = 0f;
            rotation = forward.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : Quaternion.identity;
            return true;
        }

        void EnsureCity()
        {
            if (ready || cityRoot != null)
                return;
            GameObject template = Resources.Load<GameObject>(
                TemplateResourcePath);
            if (template == null)
            {
                failure = "找不到城市试飞场模板：" + TemplateResourcePath;
                return;
            }

            cityRoot = Instantiate(template, transform, false);
            cityRoot.name = "CityTestFlightMap_城市试飞场";
            cityLab = cityRoot.GetComponent<AirCombatCityPcgLab>();
            if (cityLab == null)
            {
                failure = "城市试飞场模板缺少 AirCombatCityPcgLab。";
                Destroy(cityRoot);
                cityRoot = null;
                return;
            }

            cityLab.ConfigureRuntimeMission(
                TestCitySeed,
                AirCombatCityMission.Clearance,
                0);
            if (!cityLab.HasValidPlan || cityLab.Plan == null)
            {
                failure = "城市试飞场 PCG 校验失败：" +
                          cityLab.LastSummary;
                Destroy(cityRoot);
                cityRoot = null;
                cityLab = null;
                return;
            }

            CreateGroundCollision(cityRoot.transform, cityLab.Settings.mapSize);
            cityLab.enabled = false;
            cityRoot.SetActive(false);
            ready = true;
        }

        void ResolveSpawn(out Vector3 position, out Quaternion rotation)
        {
            AirCombatFlightRoute main = FindMainRoute();
            if (main != null && main.points != null && main.points.Length >= 3)
            {
                position = main.points[1];
                Vector3 forward = main.points[2] - main.points[1];
                forward.y = 0f;
                rotation = forward.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                    : Quaternion.identity;
                return;
            }
            position = cityLab.Plan.playerSpawn;
            rotation = Quaternion.identity;
        }

        AirCombatFlightRoute FindMainRoute()
        {
            if (cityLab == null || cityLab.Plan == null)
                return null;
            for (int i = 0; i < cityLab.Plan.routes.Count; i++)
            {
                AirCombatFlightRoute route = cityLab.Plan.routes[i];
                if (route.kind == AirCombatRouteKind.Main)
                    return route;
            }
            return null;
        }

        void DisableBuildPresentation(Rigidbody target)
        {
            RestoreBuildPresentation();
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                foreach (Collider collider in
                         root.GetComponentsInChildren<Collider>(true))
                {
                    if (collider == null ||
                        collider.transform.IsChildOf(target.transform) ||
                        (cityRoot != null &&
                         collider.transform.IsChildOf(cityRoot.transform)) ||
                        !BelongsToBuildArea(collider.transform))
                    {
                        continue;
                    }
                    buildColliderStates[collider] = collider.enabled;
                    collider.enabled = false;
                }
                foreach (Renderer renderer in
                         root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null ||
                        renderer.transform.IsChildOf(target.transform) ||
                        (cityRoot != null &&
                         renderer.transform.IsChildOf(cityRoot.transform)) ||
                        !BelongsToBuildArea(renderer.transform))
                    {
                        continue;
                    }
                    buildRendererStates[renderer] = renderer.enabled;
                    renderer.enabled = false;
                }
            }
            Physics.SyncTransforms();
        }

        static bool BelongsToBuildArea(Transform value)
        {
            return value.GetComponentInParent<AirBuildExperienceController>() !=
                   null ||
                   string.Equals(
                       value.gameObject.name,
                       "IndustrialTestPlatform",
                       StringComparison.Ordinal);
        }

        void RestoreBuildPresentation()
        {
            foreach (KeyValuePair<Collider, bool> pair in buildColliderStates)
            {
                if (pair.Key != null)
                    pair.Key.enabled = pair.Value;
            }
            foreach (KeyValuePair<Renderer, bool> pair in buildRendererStates)
            {
                if (pair.Key != null)
                    pair.Key.enabled = pair.Value;
            }
            buildColliderStates.Clear();
            buildRendererStates.Clear();
            Physics.SyncTransforms();
        }

        static void CreateGroundCollision(Transform parent, float mapSize)
        {
            var ground = new GameObject("CityTestGroundCollision_城市地面");
            ground.transform.SetParent(parent, false);
            ground.transform.localPosition = new Vector3(0f, -1.1f, 0f);
            BoxCollider collider = ground.AddComponent<BoxCollider>();
            collider.size = new Vector3(mapSize + 128f, 2f, mapSize + 128f);
        }

        void OnDestroy()
        {
            RestoreBuildPresentation();
        }
    }
}
