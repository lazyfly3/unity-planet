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
        [SerializeField] private int bakeVersion = 2;
        [SerializeField] private Vector3 playerSpawnPosition;
        [SerializeField] private Vector3 playerSpawnEuler;
        [SerializeField] private Vector3 enemySpawnPosition;
        [SerializeField] private Vector3 enemySpawnEuler;
        [SerializeField] private Vector3 battleCenter;
        [SerializeField] private float warningRadius = 1200f;
        [SerializeField] private float forfeitRadius = 1500f;

        private Renderer[] renderers = Array.Empty<Renderer>();
        private Collider[] colliders = Array.Empty<Collider>();
        private bool cached;

        public int Priority
        {
            get { return 1000; }
        }

        public int BakeVersion => bakeVersion;

        public Quaternion PreparedRotation
        {
            get { return Quaternion.Euler(playerSpawnEuler); }
        }

        public Vector3 BattleCenter
        {
            get { return battleCenter; }
        }

        public float WarningRadius
        {
            get { return warningRadius; }
        }

        public float ForfeitRadius
        {
            get { return forfeitRadius; }
        }

        private void Awake()
        {
            CacheEnvironment();
            SetEnvironmentActive(false);
        }

        public void ConfigureBaked(
            GameObject root,
            Vector3 playerPosition,
            Quaternion playerRotation,
            Vector3 enemyPosition,
            Quaternion enemyRotation,
            Vector3 center,
            float warning,
            float forfeit)
        {
            environmentRoot = root;
            bakeVersion = 2;
            playerSpawnPosition = playerPosition;
            playerSpawnEuler = playerRotation.eulerAngles;
            enemySpawnPosition = enemyPosition;
            enemySpawnEuler = enemyRotation.eulerAngles;
            battleCenter = center;
            warningRadius = Mathf.Max(100f, warning);
            forfeitRadius = Mathf.Max(warningRadius + 50f, forfeit);
            cached = false;
            CacheEnvironment();
            SetEnvironmentActive(false);
        }

        public IEnumerator Warmup(Action<bool, string> completed)
        {
            CacheEnvironment();
            yield return null;
            SetEnvironmentActive(false);
            bool valid = environmentRoot != null && colliders.Length > 0;
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
            SetEnvironmentActive(true);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Vector3 spawn = playerSpawnPosition;
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
            if (target != null)
            {
                target.position = playerSpawnPosition;
                target.rotation = PreparedRotation;
            }

            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            completed(true, playerSpawnPosition);
        }

        public void ExitFlight()
        {
            SetEnvironmentActive(false);
        }

        public bool TryGetPlayerSpawn(out Vector3 position, out Quaternion rotation)
        {
            position = playerSpawnPosition;
            rotation = Quaternion.Euler(playerSpawnEuler);
            return true;
        }

        public bool TryGetEnemySpawn(out Vector3 position, out Quaternion rotation)
        {
            position = enemySpawnPosition;
            rotation = Quaternion.Euler(enemySpawnEuler);
            return true;
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

            renderers = environmentRoot != null
                ? environmentRoot.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
            colliders = environmentRoot != null
                ? environmentRoot.GetComponentsInChildren<Collider>(true)
                : Array.Empty<Collider>();
            cached = true;
        }

        private void SetEnvironmentActive(bool active)
        {
            CacheEnvironment();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = active;
                }
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = active;
                }
            }
        }
    }
}
