using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.CombatMap
{
    /// <summary>
    /// Runtime-only CombatMap environment. The baked scene contains no camera,
    /// vehicle, UI or input component; this class only exposes geometry and poses.
    /// RC3 remains responsible for gravity, atmosphere, wind and vehicle forces.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BakedCombatMapFlightEnvironment :
        MonoBehaviour,
        IGridFlightEnvironment,
        IGridFlightEnvironmentWarmup,
        ICombatArenaProvider
    {
        [SerializeField] private GameObject environmentRoot;
        [SerializeField] private int bakeVersion = 3;
        [SerializeField] private Vector3 playerSpawnPosition;
        [SerializeField] private Vector3 playerSpawnEuler;
        [SerializeField] private Vector3 enemySpawnPosition;
        [SerializeField] private Vector3 enemySpawnEuler;
        [SerializeField] private Vector3 battleCenter;
        [SerializeField] private float warningRadius = 1200f;
        [SerializeField] private float forfeitRadius = 1500f;
        [SerializeField] private float flightCeiling = 240f;
        [SerializeField] private GameObject hordeEnvironmentRoot;
        [SerializeField] private Vector3 hordePlayerSpawnPosition;
        [SerializeField] private Vector3 hordePlayerSpawnEuler;
        [SerializeField] private Vector3 hordeEnemySpawnPosition;
        [SerializeField] private Vector3 hordeEnemySpawnEuler;
        [SerializeField] private Vector3 hordeBattleCenter;
        [SerializeField] private float hordeWarningRadius = 600f;
        [SerializeField] private float hordeForfeitRadius = 650f;
        [SerializeField] private float hordeFlightCeiling = 260f;
        [SerializeField] private CombatTestMode selectedMode =
            CombatTestMode.Duel;

        private Renderer[] duelRenderers = Array.Empty<Renderer>();
        private Collider[] duelColliders = Array.Empty<Collider>();
        private Renderer[] hordeRenderers = Array.Empty<Renderer>();
        private Collider[] hordeColliders = Array.Empty<Collider>();
        private bool cached;
        private bool environmentActive;

        public int Priority
        {
            get { return 1000; }
        }

        public int BakeVersion => bakeVersion;

        public CombatTestMode Mode => selectedMode;

        private bool UseHorde =>
            selectedMode == CombatTestMode.Horde
            && hordeEnvironmentRoot != null;

        public Quaternion PreparedRotation
        {
            get
            {
                return Quaternion.Euler(
                    UseHorde
                        ? hordePlayerSpawnEuler
                        : playerSpawnEuler);
            }
        }

        public Vector3 BattleCenter
        {
            get { return UseHorde ? hordeBattleCenter : battleCenter; }
        }

        public float WarningRadius
        {
            get { return UseHorde ? hordeWarningRadius : warningRadius; }
        }

        public float ForfeitRadius
        {
            get { return UseHorde ? hordeForfeitRadius : forfeitRadius; }
        }

        public float FlightCeiling
        {
            get
            {
                return UseHorde
                    ? hordeFlightCeiling
                    : flightCeiling;
            }
        }

        private void Awake()
        {
            CacheEnvironment();
            SetEnvironmentActive(false);
        }

        public void SetMode(CombatTestMode mode)
        {
            if (selectedMode == mode)
                return;
            bool wasActive = environmentActive;
            SetEnvironmentActive(false);
            selectedMode = mode;
            if (wasActive)
                SetEnvironmentActive(true);
        }

        public void ConfigureBaked(
            GameObject root,
            Vector3 playerPosition,
            Quaternion playerRotation,
            Vector3 enemyPosition,
            Quaternion enemyRotation,
            Vector3 center,
            float warning,
            float forfeit,
            float ceiling = 0f)
        {
            environmentRoot = root;
            bakeVersion = 3;
            playerSpawnPosition = playerPosition;
            playerSpawnEuler = playerRotation.eulerAngles;
            enemySpawnPosition = enemyPosition;
            enemySpawnEuler = enemyRotation.eulerAngles;
            battleCenter = center;
            warningRadius = Mathf.Max(100f, warning);
            forfeitRadius = Mathf.Max(warningRadius + 50f, forfeit);
            flightCeiling = Mathf.Max(center.y + 20f, ceiling);
            hordeEnvironmentRoot = null;
            cached = false;
            CacheEnvironment();
            SetEnvironmentActive(false);
        }

        public void ConfigureBakedModes(
            GameObject duelRoot,
            Vector3 duelPlayerPosition,
            Quaternion duelPlayerRotation,
            Vector3 duelEnemyPosition,
            Quaternion duelEnemyRotation,
            Vector3 duelCenter,
            float duelWarning,
            float duelForfeit,
            GameObject hordeRoot,
            Vector3 hordePlayerPosition,
            Quaternion hordePlayerRotation,
            Vector3 hordeEnemyPosition,
            Quaternion hordeEnemyRotation,
            Vector3 hordeCenter,
            float hordeWarning,
            float hordeForfeit,
            float duelCeiling,
            float hordeCeiling)
        {
            environmentRoot = duelRoot;
            playerSpawnPosition = duelPlayerPosition;
            playerSpawnEuler = duelPlayerRotation.eulerAngles;
            enemySpawnPosition = duelEnemyPosition;
            enemySpawnEuler = duelEnemyRotation.eulerAngles;
            battleCenter = duelCenter;
            warningRadius = Mathf.Max(100f, duelWarning);
            forfeitRadius = Mathf.Max(warningRadius + 20f, duelForfeit);
            flightCeiling = Mathf.Max(duelCenter.y + 20f, duelCeiling);

            hordeEnvironmentRoot = hordeRoot;
            hordePlayerSpawnPosition = hordePlayerPosition;
            hordePlayerSpawnEuler = hordePlayerRotation.eulerAngles;
            hordeEnemySpawnPosition = hordeEnemyPosition;
            hordeEnemySpawnEuler = hordeEnemyRotation.eulerAngles;
            hordeBattleCenter = hordeCenter;
            hordeWarningRadius = Mathf.Max(100f, hordeWarning);
            hordeForfeitRadius = Mathf.Max(
                hordeWarningRadius + 20f,
                hordeForfeit);
            hordeFlightCeiling = Mathf.Max(
                hordeCenter.y + 20f,
                hordeCeiling);
            bakeVersion = 4;
            selectedMode = CombatTestMode.Duel;
            cached = false;
            CacheEnvironment();
            SetEnvironmentActive(false);
        }

        public IEnumerator Warmup(Action<bool, string> completed)
        {
            CacheEnvironment();
            yield return null;
            SetEnvironmentActive(false);
            bool valid =
                ActiveRoot != null &&
                ActiveColliders.Length > 0 &&
                HasValidArenaData();
            completed(
                valid,
                valid
                    ? "CombatMapLab 已预热"
                    : "CombatMapLab 烘焙结果缺少环境碰撞");
        }

        public IEnumerator PrepareFlight(
            Rigidbody target,
            Action<bool, Vector3, string> completed)
        {
            CacheEnvironment();
            if (!HasValidArenaData())
            {
                completed(
                    false,
                    Vector3.zero,
                    "CombatMapLab 玩家与敌机出生点无效或重叠，请重新烘焙地图");
                yield break;
            }

            SetEnvironmentActive(true);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Vector3 spawn = UseHorde
                ? hordePlayerSpawnPosition
                : playerSpawnPosition;
            if (target != null)
            {
                target.position = spawn;
                target.rotation = PreparedRotation;
            }

            completed(true, spawn, "CombatMapLab 已就绪");
        }

        public IEnumerator ResetFlight(
            Rigidbody target,
            Action<bool, Vector3> completed)
        {
            SetEnvironmentActive(true);
            Vector3 spawn = UseHorde
                ? hordePlayerSpawnPosition
                : playerSpawnPosition;
            if (target != null)
            {
                target.position = spawn;
                target.rotation = PreparedRotation;
            }

            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            completed(true, spawn);
        }

        public void ExitFlight()
        {
            SetEnvironmentActive(false);
        }

        public bool TryGetPlayerSpawn(out Vector3 position, out Quaternion rotation)
        {
            position = UseHorde
                ? hordePlayerSpawnPosition
                : playerSpawnPosition;
            rotation = Quaternion.Euler(
                UseHorde
                    ? hordePlayerSpawnEuler
                    : playerSpawnEuler);
            return HasValidArenaData();
        }

        public bool TryGetEnemySpawn(out Vector3 position, out Quaternion rotation)
        {
            position = UseHorde
                ? hordeEnemySpawnPosition
                : enemySpawnPosition;
            rotation = Quaternion.Euler(
                UseHorde
                    ? hordeEnemySpawnEuler
                    : enemySpawnEuler);
            return HasValidArenaData();
        }

        private bool HasValidArenaData()
        {
            Vector3 player = UseHorde
                ? hordePlayerSpawnPosition
                : playerSpawnPosition;
            Vector3 enemy = UseHorde
                ? hordeEnemySpawnPosition
                : enemySpawnPosition;
            Vector3 playerEuler = UseHorde
                ? hordePlayerSpawnEuler
                : playerSpawnEuler;
            Vector3 enemyEuler = UseHorde
                ? hordeEnemySpawnEuler
                : enemySpawnEuler;
            return IsFinite(player) &&
                   IsFinite(enemy) &&
                   IsFinite(playerEuler) &&
                   IsFinite(enemyEuler) &&
                   (player - enemy).sqrMagnitude >= 100f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void CacheEnvironment()
        {
            if (cached)
            {
                return;
            }

            if (environmentRoot == null)
            {
                Transform child = transform.Find("CombatMapEnvironment");
                environmentRoot = child != null ? child.gameObject : null;
            }

            duelRenderers = environmentRoot != null
                ? environmentRoot.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
            duelColliders = environmentRoot != null
                ? environmentRoot.GetComponentsInChildren<Collider>(true)
                : Array.Empty<Collider>();
            hordeRenderers = hordeEnvironmentRoot != null
                ? hordeEnvironmentRoot.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
            hordeColliders = hordeEnvironmentRoot != null
                ? hordeEnvironmentRoot.GetComponentsInChildren<Collider>(true)
                : Array.Empty<Collider>();
            cached = true;
        }

        private void SetEnvironmentActive(bool active)
        {
            CacheEnvironment();
            environmentActive = active;
            SetComponentsActive(duelRenderers, duelColliders, false);
            SetComponentsActive(hordeRenderers, hordeColliders, false);
            if (!active)
                return;
            SetComponentsActive(
                UseHorde ? hordeRenderers : duelRenderers,
                UseHorde ? hordeColliders : duelColliders,
                true);
        }

        private GameObject ActiveRoot =>
            UseHorde ? hordeEnvironmentRoot : environmentRoot;

        private Collider[] ActiveColliders =>
            UseHorde ? hordeColliders : duelColliders;

        private static void SetComponentsActive(
            Renderer[] selectedRenderers,
            Collider[] selectedColliders,
            bool active)
        {
            for (int i = 0; i < selectedRenderers.Length; i++)
            {
                if (selectedRenderers[i] != null)
                    selectedRenderers[i].enabled = active;
            }
            for (int i = 0; i < selectedColliders.Length; i++)
            {
                if (selectedColliders[i] != null)
                    selectedColliders[i].enabled = active;
            }
        }
    }
}
