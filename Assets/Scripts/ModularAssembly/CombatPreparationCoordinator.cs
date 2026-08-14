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
        private sealed class PendingRequest
        {
            public CombatTestMode Mode;
            public Action<bool, string> Callback;
        }

        private CombatTestController combatController;
        private MonoBehaviour weaponCoordinator;
        private Coroutine warmupRoutine;
        private CombatPreparationState state;
        private float progress;
        private string message = "点击试飞或战斗测试后开始准备";
        private readonly System.Collections.Generic.List<PendingRequest>
            pendingRequests =
                new System.Collections.Generic.List<PendingRequest>();
        private CombatTestMode requestedMode = CombatTestMode.Duel;
        private bool commonReady;
        private bool duelReady;
        private bool hordeReady;

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

        public void MarkDirty()
        {
            state = CombatPreparationState.Idle;
            progress = 0f;
            message = "点击试飞或战斗测试后开始准备";
            commonReady = false;
            duelReady = false;
            hordeReady = false;
            requestedMode = CombatTestMode.Duel;
        }

        public void EnsureReady(Action<bool, string> callback)
        {
            EnsureReady(CombatTestMode.Duel, callback);
        }

        public bool IsReady(CombatTestMode mode)
        {
            return commonReady &&
                   (mode == CombatTestMode.Horde
                       ? hordeReady
                       : duelReady);
        }

        public void EnsureReady(
            CombatTestMode mode,
            Action<bool, string> callback)
        {
            if (IsReady(mode))
            {
                callback?.Invoke(true, message);
                return;
            }

            requestedMode = mode == CombatTestMode.Horde
                ? CombatTestMode.Horde
                : requestedMode;
            if (callback != null)
            {
                pendingRequests.Add(new PendingRequest
                {
                    Mode = mode,
                    Callback = callback
                });
            }
            if (warmupRoutine == null)
            {
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

            if (!commonReady && environmentManager != null)
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

            if (!commonReady)
            {
                progress = 0.35f;
                yield return YieldForBudget(budget);

                object structureGraph =
                    ReadMember(weaponCoordinator, "StructureGraph");
                InvokeNoArgument(
                    structureGraph as MonoBehaviour,
                    "PrepareCombatCache");
                progress = 0.45f;
                yield return YieldForBudget(budget);

                InvokeNoArgument(weaponCoordinator, "PrewarmCombatResources");
                CombatWeaponBudgetController budgetController =
                    weaponCoordinator != null
                        ? weaponCoordinator.GetComponent<CombatWeaponBudgetController>()
                        : null;
                if (budgetController != null)
                    budgetController.RefreshNow();
                commonReady = true;
            }

            if (!duelReady)
            {
                if (combatController == null)
                {
                    Finish(false, "战斗控制器未初始化，无法准备敌机池。");
                    yield break;
                }
                yield return combatController.PrepareModeResources(
                    CombatTestMode.Duel,
                    (value, text) =>
                    {
                        progress = Mathf.Lerp(0.5f, 0.7f, value);
                        message = text;
                    });
                duelReady = combatController.IsModePrepared(
                    CombatTestMode.Duel,
                    out string duelError);
                if (!duelReady)
                {
                    Finish(false, string.IsNullOrWhiteSpace(duelError)
                        ? "1v1敌机池准备失败。"
                        : duelError);
                    yield break;
                }
            }

            if (requestedMode == CombatTestMode.Horde && !hordeReady)
            {
                yield return combatController.PrepareModeResources(
                    CombatTestMode.Horde,
                    (value, text) =>
                    {
                        progress = Mathf.Lerp(0.7f, 0.98f, value);
                        message = text;
                    });
                hordeReady = combatController.IsModePrepared(
                    CombatTestMode.Horde,
                    out string hordeError);
                if (!hordeReady)
                {
                    Finish(false, string.IsNullOrWhiteSpace(hordeError)
                        ? "割草敌机池准备失败。"
                        : hordeError);
                    yield break;
                }
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
            for (int index = pendingRequests.Count - 1; index >= 0; index--)
            {
                PendingRequest request = pendingRequests[index];
                if (success && !IsReady(request.Mode))
                    continue;
                pendingRequests.RemoveAt(index);
                request.Callback?.Invoke(success, message);
            }
            if (success && pendingRequests.Count > 0 && warmupRoutine == null)
            {
                warmupRoutine = StartCoroutine(WarmupRoutine());
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
                if (combatController == null && behaviour is CombatTestController controller)
                {
                    combatController = controller;
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
