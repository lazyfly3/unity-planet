using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ModularAssembly;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.CombatMap
{
    /// <summary>
    /// Adapts CombatMapLab to the shared Modular flight lifecycle. It owns
    /// only terrain generation/collision and never writes aircraft physics.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatMapFlightEnvironmentController :
        MonoBehaviour,
        IGridFlightEnvironment,
        IGridFlightEnvironmentWarmup
    {
        [SerializeField, InspectorName("战斗地图控制器")]
        CombatMapRuntimeController map;
        readonly Dictionary<Collider, bool> buildColliderStates =
            new Dictionary<Collider, bool>();
        Collider[] spawnOverlapBuffer = new Collider[32];

        Quaternion preparedRotation = Quaternion.identity;
        Vector3 preparedPosition;
        bool hasPreparedPosition;
        Rigidbody flightBody;
        string status = "等待加载已生成场景。";

        public int Priority => 100;
        public Quaternion PreparedRotation => preparedRotation;
        public CombatMapFlightEnvelope LastEnvelope { get; private set; }
        public CombatMapScalePlan LastScalePlan { get; private set; }
        public string Status => status;

        public IEnumerator Warmup(Action<bool, string> completed)
        {
            yield return null;
            completed(true, "CombatMapLab 编辑场环境已就绪");
        }
        public bool HasMeasuredEnvelope =>
            LastEnvelope != null && LastEnvelope.measurementCompleted;

        public void Configure(CombatMapRuntimeController value)
        {
            map = value;
        }

        void OnEnable()
        {
            ResolveMap();
            if (Application.isPlaying && map != null)
                map.SetTerrainCollisionEnabled(false);
        }

        void OnDisable()
        {
            if (!Application.isPlaying)
                return;
            RestoreBuildColliders();
            if (map != null)
            {
                map.SetTerrainCollisionEnabled(false);
                map.SetRegenerationLocked(false);
            }
        }

public IEnumerator PrepareFlight(
            Rigidbody target,
            Action<bool, Vector3, string> completed)
        {
            ResolveMap();
            if (!ModularLabSceneProfile
                    .AllowsCombatMapFlightEnvironment(gameObject.scene))
            {
                Fail("当前场景没有启用战斗地图飞行环境。", completed);
                yield break;
            }
            if (target == null || target.gameObject.scene != gameObject.scene)
            {
                Fail("找不到当前场景内的模块飞船刚体。", completed);
                yield break;
            }
            if (map == null || !map.IsReady)
            {
                Fail(
                    "战斗地形尚未生成或未通过校验；请先在中文场景工具中生成并应用候选。",
                    completed);
                yield break;
            }

            flightBody = target;
            hasPreparedPosition = false;
            preparedRotation = Quaternion.identity;

            AirCombatMapSettings authored = map.CurrentSettings;
            LastEnvelope = CaptureGeometryEnvelope(target, authored);
            LastScalePlan = null;

            map.SetRegenerationLocked(true);
            map.SetTerrainCollisionEnabled(false);
            DisableBuildColliders(target);
            map.SetTerrainCollisionEnabled(true);
            Physics.SyncTransforms();
            yield return null;

            if (!map.TryGetSpawnPose(
                    true,
                    out Vector3 semanticPosition,
                    out Quaternion semanticRotation))
            {
                Fail("已生成场景没有可用的玩家出生语义点。", completed);
                yield break;
            }
            if (!TryFindSafeSpawn(
                    semanticPosition,
                    semanticRotation,
                    LastEnvelope,
                    authored,
                    out preparedPosition))
            {
                Fail("出生盆地内没有找到与地形/遮挡体分离的位置。", completed);
                yield break;
            }

            preparedRotation = semanticRotation;
            hasPreparedPosition = true;
            status =
                "已加载编辑器中保存的战斗场景；未执行自动盘旋测试，也未在运行时重建地图。" +
                " 地形碰撞仅作为静态环境启用，飞船物理参数未被修改。";
            completed?.Invoke(true, preparedPosition, status);
        }

        public IEnumerator ResetFlight(
            Rigidbody target,
            Action<bool, Vector3> completed)
        {
            if (target == null || !hasPreparedPosition ||
                map == null || !map.IsReady)
            {
                completed?.Invoke(false, Vector3.zero);
                yield break;
            }
            map.SetRegenerationLocked(true);
            map.SetTerrainCollisionEnabled(true);
            Physics.SyncTransforms();
            yield return null;
            completed?.Invoke(true, preparedPosition);
        }

        public void ExitFlight()
        {
            if (map != null)
            {
                map.SetTerrainCollisionEnabled(false);
                map.SetRegenerationLocked(false);
            }
            RestoreBuildColliders();
            flightBody = null;
            hasPreparedPosition = false;
            preparedRotation = Quaternion.identity;
            status = "已退出试飞；地形碰撞已关闭，建造环境已恢复。";
        }

bool TryFindSafeSpawn(
            Vector3 semanticPosition,
            Quaternion rotation,
            CombatMapFlightEnvelope envelope,
            AirCombatMapSettings settings,
            out Vector3 result)
        {
            result = Vector3.zero;
            Bounds local = envelope.localBounds;
            float width = Mathf.Max(
                envelope.horizontalSpan,
                Mathf.Max(local.size.x, local.size.z));
            float clearance = Mathf.Max(2f, envelope.verticalSpan * 0.15f);
            float step = Mathf.Max(8f, width);
            float maximumRadius = Mathf.Max(
                80f,
                Mathf.Min(
                    settings.mapSize * 0.12f,
                    Mathf.Max(settings.mainRouteWidth, width * 4f)));
            int rings = Mathf.Max(1, Mathf.CeilToInt(maximumRadius / step));
            int mask = LayerMaskFor("CombatTerrain") |
                       LayerMaskFor("CombatObstacle");
            if (mask == 0)
                mask = Physics.DefaultRaycastLayers;

            for (int ring = 0; ring <= rings; ring++)
            {
                int directions = ring == 0 ? 1 : 16;
                float radius = ring * step;
                for (int index = 0; index < directions; index++)
                {
                    float angle = directions == 1
                        ? 0f
                        : index / (float)directions * Mathf.PI * 2f;
                    Vector3 candidate = semanticPosition + new Vector3(
                        Mathf.Cos(angle) * radius,
                        0f,
                        Mathf.Sin(angle) * radius);
                    float ground = map.SampleCollisionHeight(candidate);
                    float downwardSupport = DownwardSupport(local, rotation);
                    candidate.y = ground + downwardSupport + clearance;
                    Vector3 boxCenter = candidate + rotation * local.center;
                    Vector3 halfExtents = local.extents + Vector3.one * 0.25f;
                    int hitCount = QuerySpawnOverlaps(
                        boxCenter,
                        halfExtents,
                        rotation,
                        mask);
                    bool blocked = false;
                    for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
                    {
                        Collider value = spawnOverlapBuffer[hitIndex];
                        if (value == null || flightBody != null &&
                            value.transform.IsChildOf(flightBody.transform))
                        {
                            continue;
                        }
                        blocked = true;
                        break;
                    }
                    if (blocked)
                        continue;

                    int terrainLayer = LayerMaskFor("CombatTerrain");
                    if (terrainLayer != 0 &&
                        Physics.Raycast(
                            candidate + Vector3.up * 500f,
                            Vector3.down,
                            out RaycastHit hit,
                            1000f,
                            terrainLayer,
                            QueryTriggerInteraction.Ignore) &&
                        Mathf.Abs(hit.point.y - ground) > 0.05f)
                    {
                        continue;
                    }
                    result = candidate;
                    return true;
                }
            }
            return false;
        }

        int QuerySpawnOverlaps(
            Vector3 center,
            Vector3 halfExtents,
            Quaternion rotation,
            int layerMask)
        {
            int count;
            while ((count = Physics.OverlapBoxNonAlloc(
                       center,
                       halfExtents,
                       spawnOverlapBuffer,
                       rotation,
                       layerMask,
                       QueryTriggerInteraction.Ignore)) >=
                   spawnOverlapBuffer.Length)
            {
                Array.Resize(
                    ref spawnOverlapBuffer,
                    spawnOverlapBuffer.Length * 2);
            }
            return count;
        }

        static CombatMapFlightEnvelope CaptureGeometryEnvelope(
            Rigidbody body,
            AirCombatMapSettings settings)
        {
            Bounds bounds = CaptureLocalBounds(body.transform);
            float horizontal = Mathf.Max(bounds.size.x, bounds.size.z);
            return new CombatMapFlightEnvelope
            {
                blueprintFingerprint = body.name,
                localBounds = bounds,
                horizontalSpan = Mathf.Max(1f, horizontal),
                verticalSpan = Mathf.Max(1f, bounds.size.y),
                hullRadius = bounds.extents.magnitude,
                measurementCompleted = false,
                usedFallback = false,
                sourceAircraftUnchanged = true,
                measuredSpeed = settings.designCombatSpeed,
                turnRadius = settings.designTurnRadius,
                confidence = 1f,
                sampleCount = 0,
                diagnostic = "仅读取机体包围盒用于出生净空；没有运行飞行探针。"
            };
        }

        static Bounds CaptureLocalBounds(Transform root)
        {
            bool initialized = false;
            Bounds result = new Bounds(Vector3.zero, Vector3.zero);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null)
                    EncapsulateWorldBounds(
                        root,
                        renderer.bounds,
                        ref result,
                        ref initialized);
            }
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null)
                    EncapsulateWorldBounds(
                        root,
                        collider.bounds,
                        ref result,
                        ref initialized);
            }
            if (!initialized)
                result = new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f));
            result.Expand(0.1f);
            return result;
        }

        static void EncapsulateWorldBounds(
            Transform root,
            Bounds world,
            ref Bounds local,
            ref bool initialized)
        {
            Vector3 minimum = world.min;
            Vector3 maximum = world.max;
            for (int mask = 0; mask < 8; mask++)
            {
                Vector3 point = new Vector3(
                    (mask & 1) == 0 ? minimum.x : maximum.x,
                    (mask & 2) == 0 ? minimum.y : maximum.y,
                    (mask & 4) == 0 ? minimum.z : maximum.z);
                Vector3 value = root.InverseTransformPoint(point);
                if (!initialized)
                {
                    local = new Bounds(value, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    local.Encapsulate(value);
                }
            }
        }

        static float DownwardSupport(
            Bounds local,
            Quaternion rotation)
        {
            Vector3 minimum = local.min;
            Vector3 maximum = local.max;
            float lowest = float.PositiveInfinity;
            for (int mask = 0; mask < 8; mask++)
            {
                Vector3 corner = new Vector3(
                    (mask & 1) == 0 ? minimum.x : maximum.x,
                    (mask & 2) == 0 ? minimum.y : maximum.y,
                    (mask & 4) == 0 ? minimum.z : maximum.z);
                lowest = Mathf.Min(lowest, (rotation * corner).y);
            }
            return Mathf.Max(0f, -lowest);
        }

        void DisableBuildColliders(Rigidbody target)
        {
            RestoreBuildColliders();
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            foreach (Collider collider in
                     root.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null ||
                    collider.transform.IsChildOf(target.transform) ||
                    (map != null && map.GeneratedRoot != null &&
                     collider.transform.IsChildOf(map.GeneratedRoot)))
                {
                    continue;
                }
                string name = collider.gameObject.name;
                if (!string.Equals(
                        name,
                        "IndustrialTestPlatform",
                        StringComparison.Ordinal) &&
                    collider.GetComponentInParent<AirBuildExperienceController>() ==
                    null)
                {
                    continue;
                }
                buildColliderStates[collider] = collider.enabled;
                collider.enabled = false;
            }
            Physics.SyncTransforms();
        }

        void RestoreBuildColliders()
        {
            foreach (KeyValuePair<Collider, bool> pair in buildColliderStates)
            {
                if (pair.Key != null)
                    pair.Key.enabled = pair.Value;
            }
            buildColliderStates.Clear();
            Physics.SyncTransforms();
        }

        void Fail(
            string message,
            Action<bool, Vector3, string> completed)
        {
            status = message;
            hasPreparedPosition = false;
            preparedRotation = Quaternion.identity;
            if (map != null)
            {
                map.SetTerrainCollisionEnabled(false);
                map.SetRegenerationLocked(false);
            }
            RestoreBuildColliders();
            flightBody = null;
            completed?.Invoke(false, Vector3.zero, message);
        }

        void ResolveMap()
        {
            if (map == null || map.gameObject.scene != gameObject.scene)
                map = FindInScene<CombatMapRuntimeController>(gameObject.scene);
        }

        static T FindInScene<T>(Scene scene) where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T value = root.GetComponentInChildren<T>(true);
                if (value != null)
                    return value;
            }
            return null;
        }

        static int LayerMaskFor(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            return layer >= 0 ? 1 << layer : 0;
        }

        static string ValidationSummary(CombatMapValidationReport report)
        {
            if (report == null)
                return "没有验证报告。";
            string first = report.violations == null
                ? string.Empty
                : report.violations
                    .Where(value => value != null)
                    .Select(value => value.message)
                    .FirstOrDefault() ?? string.Empty;
            return string.Format(
                "评分 {0:0.0}/{1:0.0}，硬错误={2}。{3}",
                report.score,
                report.minimumCommitScore,
                report.HasHardErrors ? "是" : "否",
                first);
        }


    }
}
