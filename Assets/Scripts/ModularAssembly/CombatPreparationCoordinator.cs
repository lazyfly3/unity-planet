using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    [DisallowMultipleComponent]
    public sealed class CombatPreparationCoordinator : MonoBehaviour
    {
        private const double FrameBudgetMilliseconds = 2.0;

        private FlightEnvironmentManager environmentManager;
        private MonoBehaviour combatController;
        private MonoBehaviour weaponCoordinator;
        private Coroutine warmupRoutine;
        private CombatPreparationState state;
        private float progress;
        private string message = string.Empty;
        private float dirtyAt = -1f;
        private Action<bool, string> completion;

        public CombatPreparationState State
        {
            get { return state; }
        }

        public float Progress
        {
            get { return progress; }
        }

        public string Message
        {
            get { return message; }
        }

        private void Awake()
        {
            environmentManager = FindObjectOfType<FlightEnvironmentManager>(true);
            ResolveTargets();
        }

        private void Start()
        {
            MarkDirty();
        }

        private void Update()
        {
            if (dirtyAt >= 0f && Time.unscaledTime >= dirtyAt && warmupRoutine == null)
            {
                dirtyAt = -1f;
                warmupRoutine = StartCoroutine(WarmupRoutine());
            }
        }

        public void MarkDirty()
        {
            state = CombatPreparationState.Idle;
            dirtyAt = Time.unscaledTime + 0.25f;
        }

        public void EnsureReady(Action<bool, string> callback)
        {
            if (state == CombatPreparationState.Ready)
            {
                callback(true, message);
                return;
            }

            completion += callback;
            if (warmupRoutine == null)
            {
                dirtyAt = -1f;
                warmupRoutine = StartCoroutine(WarmupRoutine());
            }
        }

        private IEnumerator WarmupRoutine()
        {
            ResolveTargets();
            state = CombatPreparationState.Warming;
            progress = 0.05f;
            message = "正在准备测试场";
            Stopwatch budget = Stopwatch.StartNew();

            if (environmentManager != null)
            {
                bool environmentReady = false;
                string environmentMessage = string.Empty;
                yield return environmentManager.Warmup(
                    (success, resultMessage) =>
                    {
                        environmentReady = success;
                        environmentMessage = resultMessage;
                    });
                if (!environmentReady)
                {
                    Finish(false, environmentMessage);
                    yield break;
                }
            }

            progress = 0.45f;
            yield return YieldForBudget(budget);

            object structureGraph =
                ReadMember(weaponCoordinator, "StructureGraph");
            InvokeNoArgument(
                structureGraph as MonoBehaviour,
                "PrepareCombatCache");
            progress = 0.58f;
            yield return YieldForBudget(budget);

            InvokeNoArgument(combatController, "PrepareEnemyPool");
            progress = 0.7f;
            yield return YieldForBudget(budget);

            InvokeNoArgument(weaponCoordinator, "PrewarmCombatResources");
            CombatWeaponBudgetController budgetController =
                weaponCoordinator != null
                    ? weaponCoordinator.GetComponent<CombatWeaponBudgetController>()
                    : null;
            if (budgetController != null)
            {
                budgetController.RefreshNow();
            }

            progress = 1f;
            Finish(true, "战斗资源已就绪");
        }

        private IEnumerator YieldForBudget(Stopwatch stopwatch)
        {
            if (stopwatch.Elapsed.TotalMilliseconds >= FrameBudgetMilliseconds)
            {
                yield return null;
                stopwatch.Restart();
            }
        }

        private void Finish(bool success, string resultMessage)
        {
            state = success ? CombatPreparationState.Ready : CombatPreparationState.Failed;
            progress = success ? 1f : progress;
            message = string.IsNullOrWhiteSpace(resultMessage)
                ? success ? "战斗资源已就绪" : "战斗资源准备失败"
                : resultMessage;
            warmupRoutine = null;
            Action<bool, string> pending = completion;
            completion = null;
            if (pending != null)
            {
                pending(success, message);
            }
        }

        private void ResolveTargets()
        {
            if (environmentManager == null)
            {
                environmentManager = FindObjectOfType<FlightEnvironmentManager>(true);
            }

            MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                string typeName = behaviour.GetType().Name;
                if (combatController == null && typeName == "CombatTestController")
                {
                    combatController = behaviour;
                }
                else if (weaponCoordinator == null && typeName == "WeaponSystemCoordinator")
                {
                    weaponCoordinator = behaviour;
                    CombatWeaponBudgetController budget = behaviour.GetComponent<CombatWeaponBudgetController>();
                    if (budget == null)
                    {
                        budget = behaviour.gameObject.AddComponent<CombatWeaponBudgetController>();
                    }

                    budget.Bind(behaviour);
                }
            }
        }

        private static void InvokeNoArgument(MonoBehaviour target, string methodName)
        {
            if (target == null)
            {
                return;
            }

            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (method != null)
            {
                method.Invoke(target, null);
            }
        }

        private static object ReadMember(
            MonoBehaviour target,
            string memberName)
        {
            if (target == null)
                return null;
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(
                memberName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            if (property != null)
                return property.GetValue(target, null);
            FieldInfo field = type.GetField(
                memberName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            return field != null ? field.GetValue(target) : null;
        }
    }

    internal static class CombatPreparationRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                    .IndexOf("ModularAssemblyLab", StringComparison.OrdinalIgnoreCase) < 0 ||
                UnityEngine.Object.FindObjectOfType<CombatPreparationCoordinator>(true) != null)
            {
                return;
            }

            GameObject host = new GameObject("CombatPreparationCoordinator");
            host.AddComponent<CombatPreparationCoordinator>();
        }
    }
}
