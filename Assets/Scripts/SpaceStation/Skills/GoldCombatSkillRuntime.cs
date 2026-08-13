using System;
using System.Collections.Generic;
using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.SpaceStation.Skills
{
    /// <summary>
    /// Transient combat modifiers. This layer never changes module topology,
    /// mass, joints or saved blueprints.
    /// </summary>
    public static class PlayerSkillCombatEffects
    {
        static Transform owner;
        static float defenseUntil;
        static float damageUntil;
        static float fireRateUntil;
        static float freezeUntil;
        static float defenseMultiplier = 1f;
        static float damageMultiplier = 1f;
        static float fireRateMultiplier = 1f;
        static bool destructionRoundArmed;
        static float destructionRadius;
        static float destructionDepth;
        static float destructionDamage;
        static float destructionRange;

        public static bool NoCooldownForTesting { get; private set; }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            owner = null;
            defenseUntil = 0f;
            damageUntil = 0f;
            fireRateUntil = 0f;
            freezeUntil = 0f;
            defenseMultiplier = 1f;
            damageMultiplier = 1f;
            fireRateMultiplier = 1f;
            destructionRoundArmed = false;
            destructionRadius = 0f;
            destructionDepth = 0f;
            destructionDamage = 0f;
            destructionRange = 0f;
            NoCooldownForTesting = false;
        }

        public static void SetNoCooldownForTesting(bool value)
        {
            NoCooldownForTesting = value;
        }

        public static void ApplyDefense(
            Transform player,
            float duration,
            float receivedDamageMultiplier)
        {
            Bind(player);
            defenseUntil = Mathf.Max(
                defenseUntil,
                Time.unscaledTime + Mathf.Max(0f, duration));
            defenseMultiplier = Mathf.Clamp(
                receivedDamageMultiplier,
                0.05f,
                1f);
        }

        public static void ApplyDamageBoost(
            Transform player,
            float duration,
            float multiplier)
        {
            Bind(player);
            damageUntil = Mathf.Max(
                damageUntil,
                Time.unscaledTime + Mathf.Max(0f, duration));
            damageMultiplier = Mathf.Max(1f, multiplier);
        }

        public static void ApplyFireRateBoost(
            Transform player,
            float duration,
            float multiplier)
        {
            Bind(player);
            fireRateUntil = Mathf.Max(
                fireRateUntil,
                Time.unscaledTime + Mathf.Max(0f, duration));
            fireRateMultiplier = Mathf.Max(1f, multiplier);
        }

        public static void ArmDestructionRound(
            Transform player,
            float radius,
            float depth,
            float bonusDamage)
        {
            Bind(player);
            destructionRoundArmed = true;
            destructionRadius = Mathf.Clamp(radius, 4f, 60f);
            destructionDepth = Mathf.Clamp(depth, 1f, 24f);
            destructionDamage = Mathf.Max(0f, bonusDamage);
            destructionRange = 0f;
        }

        public static void ArmCrescentBlade(
            Transform player,
            float bladeWidth,
            float bladeDamage,
            float travelSpeed,
            float range)
        {
            Bind(player);
            destructionRoundArmed = true;
            destructionRadius = Mathf.Clamp(bladeWidth, 12f, 52f);
            destructionDepth = Mathf.Clamp(travelSpeed, 80f, 320f);
            destructionDamage = Mathf.Max(1f, bladeDamage);
            destructionRange = Mathf.Clamp(range, 80f, 900f);
        }

        public static void FreezeEnemies(float duration)
        {
            freezeUntil = Mathf.Max(
                freezeUntil,
                Time.unscaledTime + Mathf.Max(0f, duration));
        }

        public static bool AreEnemiesFrozen =>
            Time.unscaledTime < freezeUntil;

        public static bool IsDestructionRoundArmed(Transform target)
        {
            return destructionRoundArmed && IsOwner(target);
        }

        public static void CancelDestructionRound(Transform target)
        {
            if (owner != null && target != null && !IsOwner(target))
                return;
            destructionRoundArmed = false;
            destructionRadius = 0f;
            destructionDepth = 0f;
            destructionDamage = 0f;
            destructionRange = 0f;
        }

        public static float ScaleIncomingDamage(
            Transform target,
            float amount)
        {
            return IsOwner(target) && Time.unscaledTime < defenseUntil
                ? Mathf.Max(0f, amount) * defenseMultiplier
                : Mathf.Max(0f, amount);
        }

        public static float ScaleOutgoingDamage(
            GameObject source,
            float amount)
        {
            return IsOwner(source == null ? null : source.transform) &&
                   Time.unscaledTime < damageUntil
                ? Mathf.Max(0f, amount) * damageMultiplier
                : Mathf.Max(0f, amount);
        }

        public static float FireRateMultiplier(GameObject source)
        {
            return IsOwner(source == null ? null : source.transform) &&
                   Time.unscaledTime < fireRateUntil
                ? fireRateMultiplier
                : 1f;
        }

        public static bool TryConsumeDestructionRound(
            GameObject source,
            out float radius,
            out float depth,
            out float bonusDamage)
        {
            radius = 0f;
            depth = 0f;
            bonusDamage = 0f;
            if (!destructionRoundArmed ||
                !IsOwner(source == null ? null : source.transform))
                return false;
            destructionRoundArmed = false;
            radius = destructionRadius;
            depth = destructionDepth;
            bonusDamage = destructionDamage;
            destructionRadius = 0f;
            destructionDepth = 0f;
            destructionDamage = 0f;
            destructionRange = 0f;
            return true;
        }

        public static bool TryConsumeCrescentBlade(
            GameObject source,
            out float bladeWidth,
            out float bladeDamage,
            out float travelSpeed,
            out float range)
        {
            bladeWidth = 0f;
            bladeDamage = 0f;
            travelSpeed = 0f;
            range = 0f;
            if (!destructionRoundArmed ||
                !IsOwner(source == null ? null : source.transform))
            {
                return false;
            }

            destructionRoundArmed = false;
            bladeWidth = destructionRadius;
            travelSpeed = destructionDepth;
            bladeDamage = destructionDamage;
            range = destructionRange;
            destructionRadius = 0f;
            destructionDepth = 0f;
            destructionDamage = 0f;
            destructionRange = 0f;
            return true;
        }

        static void Bind(Transform value)
        {
            if (value != null)
                owner = value;
        }

        static bool IsOwner(Transform value)
        {
            if (owner == null || value == null)
                return false;
            if (value == owner || value.IsChildOf(owner))
                return true;
            VehicleStructureGraph sourceGraph =
                value.GetComponentInParent<VehicleStructureGraph>();
            return sourceGraph != null && sourceGraph.transform == owner;
        }
    }

    [DisallowMultipleComponent]
    public sealed class GoldCombatSkillRuntime : MonoBehaviour
    {
        readonly Collider[] overlaps = new Collider[256];
        readonly RaycastHit[] sightHits = new RaycastHit[48];
        readonly HashSet<int> targetIds = new HashSet<int>();

        VehicleStructureGraph graph;
        Camera sceneCamera;
        GameObject drone;
        Transform droneVisual;
        float droneUntil;
        float droneNextShot;
        int droneLevel;

        public void Initialize(VehicleStructureGraph target)
        {
            graph = target;
        }

        public bool TryActivate(
            string skillId,
            int level,
            out string message)
        {
            level = Mathf.Clamp(level, 1, 5);
            switch (skillId)
            {
                case PlayerSkillCatalog.DefenseMatrixId:
                    return ActivateDefense(level, out message);
                case PlayerSkillCatalog.DamageAmplifierId:
                    return ActivateDamage(level, out message);
                case PlayerSkillCatalog.SupportDroneId:
                    return ActivateDrone(level, out message);
                case PlayerSkillCatalog.FireRateOverdriveId:
                    return ActivateFireRate(level, out message);
                case PlayerSkillCatalog.TerrainBreakerRoundId:
                    return ActivateDestructionRound(level, out message);
                case PlayerSkillCatalog.AbsoluteFreezeId:
                    return ActivateFreeze(level, out message);
                default:
                    message = "技能运行时尚未接入。";
                    return false;
            }
        }

        void Update()
        {
            if (drone == null)
                return;
            if (graph == null || !graph.Active ||
                Time.unscaledTime >= droneUntil)
            {
                DestroyDrone();
                return;
            }

            Bounds bounds = graph.ResolveVisualBounds();
            float orbit = Time.unscaledTime * 75f;
            Vector3 side = Quaternion.AngleAxis(
                orbit,
                transform.up) * transform.right;
            Vector3 targetPosition = bounds.center +
                                     side * (bounds.extents.magnitude + 5f) +
                                     transform.up * 2.5f;
            drone.transform.position = Vector3.Lerp(
                drone.transform.position,
                targetPosition,
                1f - Mathf.Exp(-Time.unscaledDeltaTime * 8f));
            Vector3 forward = transform.forward;
            if (TryFindNearestEnemy(
                    drone.transform.position,
                    620f,
                    out Transform targetRoot,
                    out ISpaceDamageable damageable,
                    out Vector3 aimPoint))
            {
                forward = (aimPoint - drone.transform.position).normalized;
                if (Time.unscaledTime >= droneNextShot &&
                    HasLineOfSight(
                        drone.transform.position,
                        targetRoot,
                        aimPoint))
                {
                    droneNextShot = Time.unscaledTime +
                                    Mathf.Lerp(0.72f, 0.46f,
                                        (droneLevel - 1) / 4f);
                    float damage = 24f + (droneLevel - 1) * 8f;
                    ApplySkillDamage(
                        damageable,
                        aimPoint,
                        damage);
                    SkillBeamVisual.Spawn(
                        drone.transform.position,
                        aimPoint,
                        new Color(0.16f, 0.95f, 1f),
                        0.1f,
                        0.16f);
                }
            }
            if (forward.sqrMagnitude > 0.001f)
                drone.transform.rotation = Quaternion.Slerp(
                    drone.transform.rotation,
                    Quaternion.LookRotation(forward, transform.up),
                    1f - Mathf.Exp(-Time.unscaledDeltaTime * 9f));
        }

        void OnDestroy()
        {
            DestroyDrone();
        }

        bool ActivateDefense(int level, out string message)
        {
            if (!CanUse(out message))
                return false;
            float duration = 7f + (level - 1) * 0.75f;
            float reduction = 0.45f + (level - 1) * 0.08f;
            PlayerSkillCombatEffects.ApplyDefense(
                graph.transform,
                duration,
                1f - reduction);
            SkillShieldFieldVisual.Spawn(
                graph.transform,
                graph.ResolveVisualBounds(),
                duration);
            message = "防御矩阵启动：受到的伤害降低 " +
                      Mathf.RoundToInt(reduction * 100f) + "%";
            return true;
        }

        bool ActivateDamage(int level, out string message)
        {
            if (!CanUse(out message))
                return false;
            float duration = 8f + (level - 1) * 0.75f;
            float boost = 0.4f + (level - 1) * 0.1f;
            PlayerSkillCombatEffects.ApplyDamageBoost(
                graph.transform,
                duration,
                1f + boost);
            SkillWeaponAuraVisual.Spawn(
                graph,
                "DamageSurge",
                new Color(1f, 0.28f, 0.035f, 0.86f),
                duration,
                false);
            message = "武器增幅启动：伤害提高 " +
                      Mathf.RoundToInt(boost * 100f) + "%";
            return true;
        }

        bool ActivateFireRate(int level, out string message)
        {
            if (!CanUse(out message))
                return false;
            float duration = 8f + (level - 1) * 0.75f;
            float boost = 0.35f + (level - 1) * 0.075f;
            PlayerSkillCombatEffects.ApplyFireRateBoost(
                graph.transform,
                duration,
                1f + boost);
            SkillWeaponAuraVisual.Spawn(
                graph,
                "FireRateTurbine",
                new Color(0.1f, 0.58f, 1f, 0.82f),
                duration,
                false);
            message = "火控超频启动：射速提高 " +
                      Mathf.RoundToInt(boost * 100f) + "%";
            return true;
        }

        bool ActivateDrone(int level, out string message)
        {
            if (!CanUse(out message))
                return false;
            DestroyDrone();
            droneLevel = level;
            droneUntil = Time.unscaledTime + 11f + (level - 1) * 1.5f;
            droneNextShot = Time.unscaledTime + 0.25f;
            drone = new GameObject("TemporarySupportDrone");
            drone.transform.position = graph.ResolveVisualBounds().center +
                                       transform.right * 6f;
            GameObject model = EnemyFleetVisualLibrary.Create(
                drone.transform,
                "hull.sf_modular_pirate",
                "SupportDroneModel",
                8f,
                false);
            if (model == null || !EnsureDroneModelVisible(model))
            {
                if (model != null)
                    Destroy(model);
                BuildFallbackDrone(drone.transform);
            }
            else
            {
                droneVisual = model.transform;
                ApplySupportDronePaint(model);
            }
            if (droneVisual != null)
                DroneMaterializeVisual.Attach(droneVisual, 0.46f);
            SkillWorldSpriteVisual.SpawnSurface(
                drone.transform.position - transform.up * 1.8f,
                transform.up,
                "DroneSummonPortal",
                new Color(0.18f, 1f, 1f, 0.92f),
                0.9f,
                2.5f,
                10f,
                210f);
            message = "支援无人机入场，将持续作战 " +
                      (11f + (level - 1) * 1.5f).ToString("0.0") + " 秒";
            return true;
        }

        bool ActivateDestructionRound(int level, out string message)
        {
            if (!CanUse(out message))
                return false;
            float bladeWidth = 26f + (level - 1) * 4f;
            float damage = 160f + (level - 1) * 45f;
            PlayerSkillCombatEffects.ArmCrescentBlade(
                graph.transform,
                bladeWidth,
                damage,
                185f + (level - 1) * 8f,
                520f + (level - 1) * 45f);
            SkillWeaponAuraVisual.Spawn(
                graph,
                "CrescentBuildingCutter",
                new Color(0.12f, 0.88f, 1f, 0.95f),
                30f,
                true);
            message = "月牙光刃已装填：下一次左键从准星方向发射";
            return true;
        }

        bool ActivateFreeze(int level, out string message)
        {
            if (!CanUse(out message))
                return false;
            float duration = 2.5f + (level - 1) * 0.5f;
            PlayerSkillCombatEffects.FreezeEnemies(duration);
            VehicleCombatTeamMarker[] markers =
                FindObjectsOfType<VehicleCombatTeamMarker>();
            int frozen = 0;
            foreach (VehicleCombatTeamMarker marker in markers)
            {
                if (marker == null ||
                    marker.Team != VehicleCombatTeam.Enemy)
                    continue;
                TemporaryEnemyFreeze freeze =
                    marker.GetComponent<TemporaryEnemyFreeze>();
                if (freeze == null)
                    freeze = marker.gameObject.AddComponent<
                        TemporaryEnemyFreeze>();
                freeze.FreezeFor(duration);
                frozen++;
            }
            message = "绝对冻结：" + frozen +
                      " 架敌机停止行动 " + duration.ToString("0.0") +
                      " 秒";
            return true;
        }

        bool CanUse(out string message)
        {
            if (graph != null && graph.Active && !graph.IsVehicleDestroyed)
            {
                message = string.Empty;
                return true;
            }
            message = "技能只能在有效战斗载具上使用。";
            return false;
        }

        bool TryFindNearestEnemy(
            Vector3 origin,
            float range,
            out Transform targetRoot,
            out ISpaceDamageable target,
            out Vector3 aimPoint)
        {
            targetRoot = null;
            target = null;
            aimPoint = Vector3.zero;
            targetIds.Clear();
            float best = float.PositiveInfinity;
            int count = Physics.OverlapSphereNonAlloc(
                origin,
                range,
                overlaps,
                ~0,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider collider = overlaps[index];
                if (collider == null ||
                    collider.transform.IsChildOf(graph.transform))
                    continue;
                VehicleCombatTeamMarker marker =
                    collider.GetComponentInParent<VehicleCombatTeamMarker>();
                if (marker == null ||
                    marker.Team != VehicleCombatTeam.Enemy ||
                    !targetIds.Add(marker.GetInstanceID()))
                    continue;
                ISpaceDamageable damageable =
                    WeaponDamageUtility.FindDamageable(collider.transform);
                if (damageable == null || damageable.IsDestroyed)
                    continue;
                Vector3 point = collider.bounds.center;
                float distance = (point - origin).sqrMagnitude;
                if (distance >= best)
                    continue;
                best = distance;
                targetRoot = marker.transform;
                target = damageable;
                aimPoint = point;
            }
            return target != null;
        }

        bool HasLineOfSight(
            Vector3 origin,
            Transform targetRoot,
            Vector3 targetPoint)
        {
            Vector3 offset = targetPoint - origin;
            float distance = offset.magnitude;
            if (distance <= 0.1f)
                return true;
            int count = Physics.RaycastNonAlloc(
                origin,
                offset / distance,
                sightHits,
                distance + 1f,
                ~0,
                QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            Transform selected = null;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = sightHits[index];
                if (hit.collider == null ||
                    hit.collider.transform.IsChildOf(graph.transform) ||
                    hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                selected = hit.collider.transform;
            }
            return selected == null || selected == targetRoot ||
                   selected.IsChildOf(targetRoot) ||
                   targetRoot.IsChildOf(selected);
        }

        void ApplySkillDamage(
            ISpaceDamageable damageable,
            Vector3 point,
            float damage)
        {
            if (damageable == null || damageable.IsDestroyed)
                return;
            damageable.ApplyDamage(new SpaceDamageInfo(
                damage,
                point,
                Vector3.zero,
                SpaceDamageType.Projectile,
                graph.gameObject));
        }

        void BuildFallbackDrone(Transform parent)
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "SupportDroneFallback";
            body.transform.SetParent(parent, false);
            body.transform.localScale = new Vector3(3.6f, 0.8f, 6.8f);
            Collider collider = body.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            Renderer renderer = body.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = new Color(0.08f, 0.72f, 0.9f);
            droneVisual = body.transform;
        }

        static bool EnsureDroneModelVisible(GameObject model)
        {
            if (model == null)
                return false;
            model.SetActive(true);
            Renderer[] renderers =
                model.GetComponentsInChildren<Renderer>(true);
            bool hasVisibleRenderer = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null)
                    continue;
                renderer.gameObject.SetActive(true);
                renderer.enabled = true;
                hasVisibleRenderer = true;
            }
            return hasVisibleRenderer;
        }

        static void ApplySupportDronePaint(GameObject model)
        {
            if (model == null)
                return;
            Renderer[] modelRenderers =
                model.GetComponentsInChildren<Renderer>(true);
            MaterialPropertyBlock paint = new MaterialPropertyBlock();
            Color allyPaint = new Color(0.18f, 0.82f, 1f, 1f);
            Color allyEmission = new Color(0.02f, 1.6f, 2.6f, 1f);
            for (int index = 0; index < modelRenderers.Length; index++)
            {
                Renderer renderer = modelRenderers[index];
                if (renderer == null)
                    continue;
                Material material = renderer.sharedMaterial;
                paint.Clear();
                if (material != null && material.HasProperty("_BaseColor"))
                    paint.SetColor("_BaseColor", allyPaint);
                if (material != null && material.HasProperty("_Color"))
                    paint.SetColor("_Color", allyPaint);
                if (material != null && material.HasProperty("_EmissionColor"))
                    paint.SetColor("_EmissionColor", allyEmission);
                renderer.SetPropertyBlock(paint);
            }
        }

        void DestroyDrone()
        {
            if (drone != null)
            {
                if (Application.isPlaying &&
                    gameObject.activeInHierarchy &&
                    drone.activeInHierarchy)
                {
                    SkillWorldSpriteVisual.SpawnSurface(
                        drone.transform.position - transform.up * 1.2f,
                        transform.up,
                        "DroneSummonPortal",
                        new Color(0.08f, 0.8f, 1f, 0.75f),
                        0.55f,
                        7f,
                        1.5f,
                        -260f);
                }
                Destroy(drone);
            }
            drone = null;
            droneVisual = null;
        }
    }

    public sealed class TemporaryEnemyFreeze : MonoBehaviour
    {
        Rigidbody body;
        ModularBossCombatRuntime boss;
        bool originalKinematic;
        bool originalDetectCollisions;
        Vector3 storedVelocity;
        Vector3 storedAngularVelocity;
        float until;
        bool captured;

        public void FreezeFor(float seconds)
        {
            until = Mathf.Max(until, Time.unscaledTime + seconds);
            if (!captured)
                Capture();
        }

        void Capture()
        {
            body = GetComponent<Rigidbody>() ??
                   GetComponentInChildren<Rigidbody>();
            boss = GetComponent<ModularBossCombatRuntime>() ??
                   GetComponentInParent<ModularBossCombatRuntime>() ??
                   GetComponentInChildren<ModularBossCombatRuntime>();
            captured = true;
            boss?.SetTemporarilyFrozen(true);
            if (body == null)
                return;
            originalKinematic = body.isKinematic;
            originalDetectCollisions = body.detectCollisions;
            storedVelocity = body.velocity;
            storedAngularVelocity = body.angularVelocity;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            if (Application.isPlaying)
            {
                SkillFreezeOverlayVisual.Spawn(
                    transform,
                    Mathf.Max(0.1f, until - Time.unscaledTime));
            }
        }

        void Update()
        {
            if (Time.unscaledTime < until)
                return;
            Restore();
            Destroy(this);
        }

        void OnDestroy()
        {
            Restore();
        }

        void OnDisable()
        {
            Restore();
        }

        void Restore()
        {
            if (!captured)
                return;
            captured = false;
            if (boss == null)
            {
                boss = GetComponent<ModularBossCombatRuntime>() ??
                       GetComponentInParent<ModularBossCombatRuntime>() ??
                       GetComponentInChildren<ModularBossCombatRuntime>();
            }
            if (body == null)
            {
                boss?.SetTemporarilyFrozen(false);
                return;
            }
            HordeEnemyVehicle horde =
                GetComponent<HordeEnemyVehicle>();
            if (horde != null && !horde.IsCombatCapable)
                return;
            EnemyAirCombatVehicle duel =
                GetComponent<EnemyAirCombatVehicle>();
            if (duel != null && !duel.IsCombatCapable)
                return;
            if (boss != null && !boss.IsCombatActive)
            {
                // Mission settlement owns the inactive Boss body. A delayed
                // thaw must not turn it dynamic again after combat ended.
                body.isKinematic = true;
                body.detectCollisions = originalDetectCollisions;
                boss.SetTemporarilyFrozen(false);
                return;
            }
            body.isKinematic = originalKinematic;
            body.detectCollisions = originalDetectCollisions;
            if (!body.isKinematic)
            {
                if (boss != null)
                {
                    // The Boss state machine resumes from the paused state and
                    // rebuilds velocity through ordinary RC3 input. Restoring a
                    // pre-freeze ram velocity creates an untelegraphed lunge.
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                else
                {
                    body.velocity = storedVelocity;
                    body.angularVelocity = storedAngularVelocity;
                }
                body.WakeUp();
            }
            boss?.SetTemporarilyFrozen(false);
        }
    }

    public sealed class SkillCrescentBladeProjectile : MonoBehaviour
    {
        static readonly IComparer<RaycastHit> HitDistanceComparer =
            Comparer<RaycastHit>.Create((left, right) =>
                left.distance.CompareTo(right.distance));

        readonly RaycastHit[] hits = new RaycastHit[48];
        readonly HashSet<int> processedColliders = new HashSet<int>();
        readonly UrbanEnergyBladePass urbanPass = new UrbanEnergyBladePass();
        readonly List<Material> ownedMaterials = new List<Material>(4);

        VehicleStructureGraph ownerGraph;
        Vector3 direction;
        Vector3 up;
        float bladeWidth;
        float bladeHeight;
        float damage;
        float speed;
        float remainingDistance;
        int urbanCuts;

        public static SkillCrescentBladeProjectile Spawn(
            VehicleStructureGraph owner,
            Vector3 origin,
            Vector3 forward,
            Vector3 bladeUp,
            float width,
            float bladeDamage,
            float travelSpeed,
            float range)
        {
            var root = new GameObject("SkillCrescentBladeProjectile_月牙光刃");
            root.transform.position = origin;
            SkillCrescentBladeProjectile projectile =
                root.AddComponent<SkillCrescentBladeProjectile>();
            projectile.Initialize(
                owner,
                forward,
                bladeUp,
                width,
                bladeDamage,
                travelSpeed,
                range);
            return projectile;
        }

        void Initialize(
            VehicleStructureGraph owner,
            Vector3 forward,
            Vector3 bladeUp,
            float width,
            float bladeDamage,
            float travelSpeed,
            float range)
        {
            ownerGraph = owner;
            direction = forward.sqrMagnitude > 0.001f
                ? forward.normalized
                : Vector3.forward;
            up = Vector3.ProjectOnPlane(bladeUp, direction).normalized;
            if (up.sqrMagnitude < 0.001f)
                up = Vector3.up;
            bladeWidth = Mathf.Clamp(width, 12f, 52f);
            bladeHeight = bladeWidth * 0.46f;
            damage = Mathf.Max(1f, bladeDamage);
            speed = Mathf.Clamp(travelSpeed, 80f, 320f);
            remainingDistance = Mathf.Clamp(range, 80f, 900f);
            transform.rotation = Quaternion.LookRotation(direction, up);

            CreateBladeLayer(
                "CrescentCore_生成贴图主体",
                Vector3.zero,
                Vector3.one,
                new Color(0.84f, 1f, 1f, 1f),
                3.1f);
            CreateBladeLayer(
                "CrescentAfterimageNear_近距残像",
                Vector3.back * 2.8f,
                new Vector3(0.91f, 0.91f, 1f),
                new Color(0.08f, 0.82f, 1f, 0.38f),
                1.75f);
            CreateBladeLayer(
                "CrescentAfterimageFar_远距残像",
                Vector3.back * 5.4f,
                new Vector3(0.78f, 0.78f, 1f),
                new Color(0.05f, 0.42f, 1f, 0.20f),
                1.35f);
            SkillRingVisual.SpawnWorld(
                transform.position,
                bladeWidth * 0.19f,
                new Color(0.2f, 0.9f, 1f, 0.8f),
                0.32f,
                2);
        }

        void CreateBladeLayer(
            string objectName,
            Vector3 localPosition,
            Vector3 scaleMultiplier,
            Color tint,
            float intensity)
        {
            Material material = SkillVfxMaterialUtility.Create(
                "CrescentBuildingCutter",
                tint,
                intensity,
                0.15f);
            if (material != null)
                ownedMaterials.Add(material);
            GameObject layer = SkillVfxMaterialUtility.CreateQuad(
                objectName,
                transform,
                material);
            layer.transform.localPosition = localPosition;
            // Lead with the convex cutting edge. The source image is authored
            // in the opposite orientation, which otherwise reads backwards.
            layer.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            layer.transform.localScale = new Vector3(
                bladeWidth * scaleMultiplier.x,
                bladeHeight * scaleMultiplier.y,
                1f);
        }

        void Update()
        {
            float step = Mathf.Min(
                remainingDistance,
                speed * Mathf.Max(0f, Time.unscaledDeltaTime));
            if (step <= 0.001f)
            {
                Destroy(gameObject);
                return;
            }

            Quaternion orientation = Quaternion.LookRotation(direction, up);
            int count = Physics.BoxCastNonAlloc(
                transform.position,
                new Vector3(bladeWidth * 0.47f, bladeHeight * 0.20f, 0.7f),
                direction,
                hits,
                orientation,
                step,
                ~0,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, 0, count, HitDistanceComparer);
            bool stoppedByTerrain = false;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = hits[index];
                Collider collider = hit.collider;
                if (collider == null ||
                    !processedColliders.Add(collider.GetInstanceID()) ||
                    ownerGraph != null &&
                    collider.transform.IsChildOf(ownerGraph.transform))
                {
                    continue;
                }

                if (!urbanPass.TryClaim(collider))
                {
                    // Child colliders are created immediately by a successful
                    // slice. The current projectile must not regard those new
                    // halves as fresh targets and recursively consume all
                    // generations before the player fires the next blade.
                    continue;
                }

                Vector3 point = hit.point != Vector3.zero
                    ? hit.point
                    : collider.ClosestPoint(transform.position);
                Vector3 normal = hit.normal.sqrMagnitude > 0.001f
                    ? hit.normal
                    : -direction;
                GameObject source = ownerGraph != null
                    ? ownerGraph.gameObject
                    : gameObject;
                bool cutUrban = UrbanDestructionWorld.TryApplyEnergyBlade(
                    collider,
                    point,
                    normal,
                    direction,
                    damage,
                    bladeWidth,
                    source);
                WeaponDamageUtility.ApplyDirect(
                    collider,
                    damage,
                    point,
                    direction,
                    source);
                if (cutUrban)
                {
                    urbanCuts++;
                    SkillRingVisual.SpawnWorld(
                        point,
                        Mathf.Clamp(bladeWidth * 0.24f, 4f, 13f),
                        new Color(0.15f, 0.95f, 1f, 0.9f),
                        0.45f,
                        3);
                    if (urbanCuts >= 5)
                        stoppedByTerrain = true;
                    continue;
                }

                stoppedByTerrain |= CombatTerrainDestructionRuntime
                    .ResolveCrescentImpact(
                        collider,
                        point,
                        normal,
                        bladeWidth * 0.34f,
                        bladeWidth * 0.16f);
            }

            transform.position += direction * step;
            remainingDistance -= step;
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 24f) * 0.035f;
            transform.localScale = new Vector3(pulse, pulse, 1f);
            if (stoppedByTerrain || remainingDistance <= 0.01f)
                Destroy(gameObject);
        }

        void OnDestroy()
        {
            for (int index = 0; index < ownedMaterials.Count; index++)
                if (ownedMaterials[index] != null)
                    Destroy(ownedMaterials[index]);
        }
    }

    public static class CombatTerrainDestructionRuntime
    {
        public static bool ResolveCrescentImpact(
            Collider hitCollider,
            Vector3 point,
            Vector3 normal,
            float radius,
            float depth)
        {
            if (hitCollider == null)
                return false;
            bool hitTerrainSurface = IsTerrainSurface(hitCollider);
            bool changed = TryDeformVoxel(
                hitCollider,
                point,
                Mathf.Clamp(radius, 4f, 18f));
            changed |= TryDeformRuntimeMeshes(
                hitCollider,
                point,
                normal,
                Mathf.Clamp(radius, 4f, 18f),
                Mathf.Clamp(depth, 2f, 12f),
                out bool foundTerrainSurface);
            hitTerrainSurface |= foundTerrainSurface;
            // Imported meshes and Unity runtime static batches can be valid
            // terrain collision surfaces while keeping their CPU vertex data
            // unreadable.  The crescent must still stop and play its cut impact
            // there; only the optional vertex depression is skipped.
            if (!changed && !hitTerrainSurface)
                return false;
            SkillWorldSpriteVisual.SpawnSurface(
                point + normal * 0.22f,
                normal,
                "CrescentBuildingCutter",
                new Color(0.25f, 0.95f, 1f, 0.88f),
                0.62f,
                radius * 0.35f,
                radius * 1.35f,
                0f);
            return true;
        }

        public static void ResolveImpact(
            Collider hitCollider,
            Vector3 point,
            Vector3 normal,
            GameObject source,
            float radius,
            float depth,
            float bonusDamage)
        {
            if (bonusDamage > 0f)
                WeaponDamageUtility.ApplyExplosion(
                    point,
                    radius * 1.35f,
                    bonusDamage,
                    source == null ? null : source.transform,
                    source);
            // 城市建筑使用确定性预裂单元，不进入下方的自然地形网格变形。
            // 两条路径共享破坏弹，但互不改写彼此的物理表示。
            bool changed = UrbanDestructionWorld.TryApplyDemolition(
                hitCollider,
                point,
                normal,
                -normal,
                bonusDamage,
                radius,
                source);
            changed |= TryDeformVoxel(
                hitCollider,
                point,
                radius);
            changed |= TryDeformRuntimeMeshes(
                hitCollider,
                point,
                normal,
                radius,
                depth);
            Color impactColor = changed
                ? new Color(1f, 0.4f, 0.025f, 0.96f)
                : new Color(1f, 0.68f, 0.08f, 0.84f);
            SkillWorldSpriteVisual.SpawnSurface(
                point + normal * 0.24f,
                normal,
                "TerrainBreakerImpact",
                impactColor,
                1.05f,
                radius * 0.35f,
                radius * 2.55f,
                72f);
            SkillWorldSpriteVisual.SpawnSurface(
                point + normal * 0.3f,
                normal,
                "DamageSurge",
                new Color(1f, 0.24f, 0.015f, 0.5f),
                0.55f,
                radius * 0.18f,
                radius * 1.25f,
                -180f);
        }

        static bool TryDeformVoxel(
            Collider hitCollider,
            Vector3 point,
            float radius)
        {
            VoxelWorld world = hitCollider == null
                ? UnityEngine.Object.FindObjectOfType<VoxelWorld>()
                : hitCollider.GetComponentInParent<VoxelWorld>();
            if (world == null)
                return false;
            Vector3 local = world.transform.InverseTransformPoint(point);
            float scale = Mathf.Max(
                0.01f,
                world.transform.lossyScale.x);
            int voxelRadius = Mathf.Clamp(
                Mathf.CeilToInt(radius / scale),
                2,
                18);
            Vector3Int center = new Vector3Int(
                Mathf.FloorToInt(local.x),
                Mathf.FloorToInt(local.y),
                Mathf.FloorToInt(local.z));
            bool changed = false;
            int radiusSquared = voxelRadius * voxelRadius;
            for (int x = -voxelRadius; x <= voxelRadius; x++)
            for (int y = -voxelRadius; y <= voxelRadius; y++)
            for (int z = -voxelRadius; z <= voxelRadius; z++)
            {
                if (x * x + y * y + z * z > radiusSquared)
                    continue;
                changed |= world.SetVoxel(
                    center.x + x,
                    center.y + y,
                    center.z + z,
                    VoxelTypes.Air);
            }
            return changed;
        }

        static bool TryDeformRuntimeMeshes(
            Collider hitCollider,
            Vector3 point,
            Vector3 normal,
            float radius,
            float depth)
        {
            return TryDeformRuntimeMeshes(
                hitCollider,
                point,
                normal,
                radius,
                depth,
                out _);
        }

        static bool TryDeformRuntimeMeshes(
            Collider hitCollider,
            Vector3 point,
            Vector3 normal,
            float radius,
            float depth,
            out bool foundTerrainSurface)
        {
            foundTerrainSurface = false;
            if (hitCollider == null ||
                VehicleCombatTeamUtility.Resolve(hitCollider.transform) !=
                VehicleCombatTeam.Neutral)
                return false;

            var filters = new HashSet<MeshFilter>();
            AddTerrainFilter(hitCollider, filters);
            Collider[] nearby = Physics.OverlapSphere(
                point,
                radius,
                ~0,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < nearby.Length; index++)
                AddTerrainFilter(nearby[index], filters);

            // CombatMap uses separate objects for rendering and collision:
            // TerrainChunks/Chunk_* own the visible meshes while the sibling
            // CollisionSurface owns one combined MeshCollider. Include the
            // nearby visible chunks explicitly so both representations deform.
            Transform terrainFamily = FindTerrainFamily(hitCollider.transform);
            if (terrainFamily != null)
            {
                MeshFilter[] familyFilters =
                    terrainFamily.GetComponentsInChildren<MeshFilter>(true);
                for (int index = 0; index < familyFilters.Length; index++)
                {
                    MeshFilter filter = familyFilters[index];
                    if (filter != null && filter.sharedMesh != null &&
                        LooksLikeTerrain(filter) &&
                        IsNearImpact(filter, point, radius))
                    {
                        filters.Add(filter);
                    }
                }
            }

            foundTerrainSurface = filters.Count > 0;

            bool changed = false;
            var deformedMeshes = new HashSet<Mesh>();
            foreach (MeshFilter filter in filters)
            {
                Mesh mesh = filter == null ? null : filter.sharedMesh;
                if (mesh == null || !deformedMeshes.Add(mesh))
                    continue;
                bool filterChanged = DeformRuntimeMesh(
                    filter,
                    point,
                    normal,
                    radius,
                    depth);
                changed |= filterChanged;
                if (filterChanged)
                    RefreshMeshCollider(filter.GetComponent<MeshCollider>(), mesh);
            }

            // The combined combat-map collider intentionally has no
            // MeshFilter. Deform it separately so the newly visible crater is
            // also a real navigable depression instead of cosmetic geometry.
            MeshCollider directCollider = hitCollider as MeshCollider;
            Mesh collisionMesh = directCollider == null
                ? null
                : directCollider.sharedMesh;
            if (directCollider != null && collisionMesh != null &&
                LooksLikeTerrain(directCollider) &&
                deformedMeshes.Add(collisionMesh))
            {
                foundTerrainSurface = true;
                bool colliderChanged = DeformRuntimeMesh(
                    directCollider.transform,
                    collisionMesh,
                    point,
                    normal,
                    radius,
                    depth);
                changed |= colliderChanged;
                if (colliderChanged)
                    RefreshMeshCollider(directCollider, collisionMesh);
            }
            return changed;
        }

        static Transform FindTerrainFamily(Transform start)
        {
            Transform current = start;
            Transform fallback = start == null ? null : start.parent;
            for (int depth = 0; current != null && depth < 6; depth++)
            {
                string value = current.name.ToLowerInvariant();
                if (value.Contains("terrainchunks") ||
                    value.Contains("infiniteterrain") ||
                    value.Contains("generatedcombatmap"))
                {
                    return current;
                }
                current = current.parent;
            }
            return fallback;
        }

        static bool IsNearImpact(
            MeshFilter filter,
            Vector3 point,
            float radius)
        {
            if (filter == null)
                return false;
            Renderer renderer = filter.GetComponent<Renderer>();
            if (renderer != null)
                return renderer.bounds.SqrDistance(point) <= radius * radius;
            Bounds localBounds = filter.sharedMesh.bounds;
            Vector3 worldCenter =
                filter.transform.TransformPoint(localBounds.center);
            float allowance = radius +
                              localBounds.extents.magnitude *
                              filter.transform.lossyScale.magnitude;
            return (worldCenter - point).sqrMagnitude <= allowance * allowance;
        }

        static void AddTerrainFilter(
            Collider candidate,
            HashSet<MeshFilter> filters)
        {
            if (candidate == null || filters == null ||
                VehicleCombatTeamUtility.Resolve(candidate.transform) !=
                VehicleCombatTeam.Neutral)
                return;
            MeshFilter filter = candidate.GetComponent<MeshFilter>() ??
                                candidate.GetComponentInParent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null ||
                !LooksLikeTerrain(filter))
                return;
            filters.Add(filter);
        }

        static bool DeformRuntimeMesh(
            MeshFilter filter,
            Vector3 point,
            Vector3 normal,
            float radius,
            float depth)
        {
            if (filter == null || filter.sharedMesh == null)
                return false;
            return DeformRuntimeMesh(
                filter.transform,
                filter.sharedMesh,
                point,
                normal,
                radius,
                depth);
        }

        static bool DeformRuntimeMesh(
            Transform meshTransform,
            Mesh mesh,
            Vector3 point,
            Vector3 normal,
            float radius,
            float depth)
        {
            if (meshTransform == null || mesh == null)
                return false;
            // Accessing mesh.vertices on a non-readable imported/static-batch
            // mesh logs a Unity error before C# can recover.  Urban buildings
            // are handled by UrbanDestructionWorld before this fallback; this
            // path is only the optional deformation used by natural terrain.
            if (!mesh.isReadable)
                return false;
            Vector3[] vertices = mesh.vertices;
            if (vertices == null || vertices.Length == 0)
                return false;
            Vector3 localNormal = meshTransform.InverseTransformDirection(
                normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up)
                .normalized;
            bool changed = false;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 worldVertex =
                    meshTransform.TransformPoint(vertices[index]);
                float distance = Vector3.Distance(worldVertex, point);
                if (distance > radius)
                    continue;
                float t = 1f - distance / radius;
                float bowl = t * t * (3f - 2f * t);
                vertices[index] -= localNormal * depth * bowl;
                changed = true;
            }
            if (!changed)
                return false;
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return true;
        }

        static void RefreshMeshCollider(
            MeshCollider collider,
            Mesh mesh)
        {
            if (collider == null || mesh == null)
                return;
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
        }

        static bool IsTerrainSurface(Collider collider)
        {
            if (collider == null)
                return false;
            if (collider.GetComponentInParent<VoxelWorld>() != null)
                return true;
            MeshCollider meshCollider = collider as MeshCollider;
            if (meshCollider != null && LooksLikeTerrain(meshCollider))
                return true;
            MeshFilter filter = collider.GetComponent<MeshFilter>() ??
                                collider.GetComponentInParent<MeshFilter>();
            return filter != null && LooksLikeTerrain(filter);
        }

        static bool LooksLikeTerrain(MeshFilter filter)
        {
            if (filter == null)
                return false;
            return LooksLikeTerrain(
                filter.transform,
                filter.sharedMesh);
        }

        static bool LooksLikeTerrain(MeshCollider collider)
        {
            if (collider == null)
                return false;
            return LooksLikeTerrain(
                collider.transform,
                collider.sharedMesh);
        }

        static bool LooksLikeTerrain(Transform target, Mesh mesh)
        {
            if (target == null)
                return false;
            string value = target.gameObject.name + " " +
                           (mesh == null ? string.Empty : mesh.name);
            Transform ancestor = target.parent;
            for (int depth = 0; ancestor != null && depth < 3; depth++)
            {
                value += " " + ancestor.name;
                ancestor = ancestor.parent;
            }
            value = value.ToLowerInvariant();
            return value.Contains("terrain") ||
                   value.Contains("ground") ||
                   value.Contains("mountain") ||
                   value.Contains("landscape") ||
                   value.Contains("planetlab") ||
                   value.Contains("infinitechunk");
        }
    }

    static class SkillVfxMaterialUtility
    {
        const string ResourceRoot = "UI/Skills/VFX/";

        public static Material Create(
            string textureName,
            Color tint,
            float intensity = 1.35f,
            float rimStrength = 0f)
        {
            Shader shader = Shader.Find("UnityPlanet/SkillWorldVfx") ??
                            Shader.Find("Sprites/Default");
            if (shader == null)
                return null;
            Material material = new Material(shader)
            {
                name = "RuntimeSkillVfx_" + textureName,
                renderQueue = 3020
            };
            Texture2D texture = Resources.Load<Texture2D>(
                ResourceRoot + textureName);
            if (texture != null)
            {
                texture.wrapMode = TextureWrapMode.Repeat;
                material.mainTexture = texture;
            }
            if (material.HasProperty("_Tint"))
                material.SetColor("_Tint", tint);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", tint);
            if (material.HasProperty("_Intensity"))
                material.SetFloat("_Intensity", intensity);
            if (material.HasProperty("_RimStrength"))
                material.SetFloat("_RimStrength", rimStrength);
            return material;
        }

        public static GameObject CreateQuad(
            string name,
            Transform parent,
            Material material)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            if (parent != null)
                quad.transform.SetParent(parent, false);
            Collider collider = quad.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                UnityEngine.Object.Destroy(collider);
            }
            Renderer renderer = quad.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
            return quad;
        }

        public static void SetTint(Material material, Color color)
        {
            if (material == null)
                return;
            if (material.HasProperty("_Tint"))
                material.SetColor("_Tint", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }
    }

    public sealed class SkillWorldSpriteVisual : MonoBehaviour
    {
        Material material;
        Transform follow;
        Vector3 followLocalPoint;
        Quaternion surfaceRotation;
        Color tint;
        float startedAt;
        float duration;
        float startSize;
        float endSize;
        float spinRate;
        bool billboard;

        public static SkillWorldSpriteVisual SpawnSurface(
            Vector3 position,
            Vector3 normal,
            string textureName,
            Color color,
            float seconds,
            float initialSize,
            float finalSize,
            float degreesPerSecond)
        {
            GameObject root = SkillVfxMaterialUtility.CreateQuad(
                "SkillSurfaceVfx_" + textureName,
                null,
                null);
            root.transform.position = position;
            SkillWorldSpriteVisual visual =
                root.AddComponent<SkillWorldSpriteVisual>();
            visual.Initialize(
                textureName,
                color,
                seconds,
                initialSize,
                finalSize,
                degreesPerSecond,
                false,
                null,
                position,
                normal);
            return visual;
        }

        public static SkillWorldSpriteVisual SpawnFollowingBillboard(
            Transform target,
            Vector3 worldPosition,
            string textureName,
            Color color,
            float seconds,
            float initialSize,
            float finalSize,
            float degreesPerSecond)
        {
            GameObject root = SkillVfxMaterialUtility.CreateQuad(
                "SkillBillboardVfx_" + textureName,
                null,
                null);
            root.transform.position = worldPosition;
            SkillWorldSpriteVisual visual =
                root.AddComponent<SkillWorldSpriteVisual>();
            visual.Initialize(
                textureName,
                color,
                seconds,
                initialSize,
                finalSize,
                degreesPerSecond,
                true,
                target,
                worldPosition,
                Vector3.forward);
            return visual;
        }

        void Initialize(
            string textureName,
            Color color,
            float seconds,
            float initialSize,
            float finalSize,
            float degreesPerSecond,
            bool faceCamera,
            Transform target,
            Vector3 worldPosition,
            Vector3 normal)
        {
            tint = color;
            duration = Mathf.Max(0.08f, seconds);
            startSize = Mathf.Max(0.01f, initialSize);
            endSize = Mathf.Max(0.01f, finalSize);
            spinRate = degreesPerSecond;
            billboard = faceCamera;
            follow = target;
            followLocalPoint = target == null
                ? Vector3.zero
                : target.InverseTransformPoint(worldPosition);
            Vector3 safeNormal = normal.sqrMagnitude > 0.001f
                ? normal.normalized
                : Vector3.up;
            surfaceRotation = Quaternion.FromToRotation(
                Vector3.forward,
                safeNormal);
            material = SkillVfxMaterialUtility.Create(
                textureName,
                tint,
                1.45f);
            Renderer renderer = GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
            transform.localScale = Vector3.one * startSize;
            startedAt = Time.unscaledTime;
        }

        void Update()
        {
            if (follow != null)
            {
                if (!follow.gameObject.activeInHierarchy)
                {
                    Destroy(gameObject);
                    return;
                }
                transform.position = follow.TransformPoint(followLocalPoint);
            }

            float progress = Mathf.Clamp01(
                (Time.unscaledTime - startedAt) / duration);
            if (progress >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            float smooth = progress * progress * (3f - 2f * progress);
            float size = Mathf.Lerp(startSize, endSize, smooth);
            transform.localScale = Vector3.one * size;
            float angle = (Time.unscaledTime - startedAt) * spinRate;
            Camera camera = Camera.main;
            if (billboard && camera != null)
            {
                Vector3 direction = transform.position -
                                    camera.transform.position;
                if (direction.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.LookRotation(
                        direction.normalized,
                        camera.transform.up) *
                        Quaternion.AngleAxis(angle, Vector3.forward);
            }
            else
            {
                transform.rotation = surfaceRotation *
                                     Quaternion.AngleAxis(
                                         angle,
                                         Vector3.forward);
            }
            float fadeIn = Mathf.Clamp01(progress / 0.12f);
            float fadeOut = Mathf.Clamp01((1f - progress) / 0.28f);
            Color visible = tint;
            visible.a *= Mathf.Min(fadeIn, fadeOut);
            SkillVfxMaterialUtility.SetTint(material, visible);
        }

        void OnDestroy()
        {
            if (material != null)
                Destroy(material);
        }
    }

    public sealed class SkillShieldFieldVisual : MonoBehaviour
    {
        Transform target;
        Vector3 targetLocalCenter;
        Vector3 shellSize;
        GameObject shell;
        Material material;
        float startedAt;
        float duration;

        public static void Spawn(
            Transform target,
            Bounds bounds,
            float seconds)
        {
            if (target == null)
                return;
            GameObject root = new GameObject("SkillDefenseField");
            SkillShieldFieldVisual visual =
                root.AddComponent<SkillShieldFieldVisual>();
            visual.Initialize(target, bounds, seconds);
        }

        void Initialize(Transform owner, Bounds bounds, float seconds)
        {
            target = owner;
            targetLocalCenter = target.InverseTransformPoint(bounds.center);
            shellSize = bounds.size + Vector3.one * 5.5f;
            shellSize.x = Mathf.Max(5f, shellSize.x);
            shellSize.y = Mathf.Max(5f, shellSize.y);
            shellSize.z = Mathf.Max(5f, shellSize.z);
            duration = Mathf.Max(0.2f, seconds);
            startedAt = Time.unscaledTime;

            shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shell.name = "HexShieldShell";
            shell.transform.SetParent(transform, false);
            Collider collider = shell.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            material = SkillVfxMaterialUtility.Create(
                "ShieldHexField",
                new Color(0.06f, 0.82f, 1f, 0.3f),
                1.25f,
                0.34f);
            if (material != null)
            {
                material.mainTextureScale = new Vector2(2.8f, 1.7f);
                if (material.HasProperty("_ScrollX"))
                    material.SetFloat("_ScrollX", 0.025f);
                if (material.HasProperty("_ScrollY"))
                    material.SetFloat("_ScrollY", -0.012f);
            }
            Renderer renderer = shell.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
            shell.transform.localScale = shellSize;

            float pulseSize = Mathf.Max(
                shellSize.x,
                Mathf.Max(shellSize.y, shellSize.z));
            SkillWorldSpriteVisual.SpawnFollowingBillboard(
                target,
                bounds.center,
                "DefensePulse",
                new Color(0.12f, 0.94f, 1f, 0.66f),
                0.72f,
                pulseSize * 0.32f,
                pulseSize * 1.38f,
                105f);
        }

        void Update()
        {
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                Destroy(gameObject);
                return;
            }
            float elapsed = Time.unscaledTime - startedAt;
            if (elapsed >= duration)
            {
                Destroy(gameObject);
                return;
            }
            transform.position = target.TransformPoint(targetLocalCenter);
            transform.rotation = target.rotation * Quaternion.Euler(
                0f,
                elapsed * 13f,
                elapsed * -5f);
            float breathe = 1f + Mathf.Sin(elapsed * 4.2f) * 0.018f;
            shell.transform.localScale = shellSize * breathe;
            Color visible = new Color(0.06f, 0.82f, 1f, 0.3f);
            visible.a *= Mathf.Clamp01((duration - elapsed) / 0.45f);
            SkillVfxMaterialUtility.SetTint(material, visible);
        }

        void OnDestroy()
        {
            if (material != null)
                Destroy(material);
        }
    }

    public sealed class SkillWeaponAuraVisual : MonoBehaviour
    {
        sealed class AnchorLayer
        {
            public Transform Target;
            public GameObject Aura;
            public float SpinDirection;
        }

        sealed class FlowLayer
        {
            public int AnchorIndex;
            public GameObject Ribbon;
            public float Phase;
        }

        readonly List<AnchorLayer> anchors = new List<AnchorLayer>();
        readonly List<FlowLayer> flows = new List<FlowLayer>();
        VehicleStructureGraph graph;
        Material auraMaterial;
        Material ribbonMaterial;
        Color tint;
        Vector3 graphLocalCenter;
        float startedAt;
        float duration;
        float auraSize;
        bool whileDestructionRoundArmed;

        public static void Spawn(
            VehicleStructureGraph graph,
            string auraTexture,
            Color color,
            float seconds,
            bool whileArmed)
        {
            if (graph == null)
                return;
            SkillWeaponAuraVisual[] existing =
                FindObjectsOfType<SkillWeaponAuraVisual>();
            for (int index = 0; index < existing.Length; index++)
                if (existing[index] != null &&
                    existing[index].graph == graph &&
                    existing[index].whileDestructionRoundArmed == whileArmed)
                    Destroy(existing[index].gameObject);

            GameObject root = new GameObject(
                whileArmed
                    ? "SkillTerrainBreakerCharge"
                    : "SkillWeaponAura");
            SkillWeaponAuraVisual visual =
                root.AddComponent<SkillWeaponAuraVisual>();
            visual.Initialize(
                graph,
                auraTexture,
                color,
                seconds,
                whileArmed);
        }

        void Initialize(
            VehicleStructureGraph owner,
            string auraTexture,
            Color color,
            float seconds,
            bool whileArmed)
        {
            graph = owner;
            tint = color;
            duration = Mathf.Max(0.1f, seconds);
            whileDestructionRoundArmed = whileArmed;
            startedAt = Time.unscaledTime;
            Bounds bounds = graph.ResolveVisualBounds();
            graphLocalCenter = graph.transform.InverseTransformPoint(
                bounds.center);
            auraSize = Mathf.Clamp(
                bounds.extents.magnitude * 0.2f,
                1.1f,
                4.5f);
            auraMaterial = SkillVfxMaterialUtility.Create(
                auraTexture,
                tint,
                1.35f);
            ribbonMaterial = SkillVfxMaterialUtility.Create(
                "EnergyRibbon",
                tint,
                1.7f);

            GridModuleView[] views =
                graph.GetComponentsInChildren<GridModuleView>(true);
            for (int index = 0;
                 index < views.Length && anchors.Count < 6;
                 index++)
            {
                GridModuleView view = views[index];
                if (view == null || !WeaponProfileLibrary.IsWeapon(view))
                    continue;
                AddAnchor(view.transform, anchors.Count);
            }
            if (anchors.Count == 0)
                AddAnchor(graph.transform, 0);

            int flowCount = Mathf.Min(8, anchors.Count * 2);
            for (int index = 0; index < flowCount; index++)
            {
                GameObject ribbon = SkillVfxMaterialUtility.CreateQuad(
                    "WeaponEnergyFlow_" + index,
                    transform,
                    ribbonMaterial);
                flows.Add(new FlowLayer
                {
                    AnchorIndex = index % anchors.Count,
                    Ribbon = ribbon,
                    Phase = index / (float)Mathf.Max(1, flowCount)
                });
            }
        }

        void AddAnchor(Transform target, int index)
        {
            GameObject aura = SkillVfxMaterialUtility.CreateQuad(
                "WeaponAura_" + index,
                transform,
                auraMaterial);
            anchors.Add(new AnchorLayer
            {
                Target = target,
                Aura = aura,
                SpinDirection = index % 2 == 0 ? 1f : -1f
            });
        }

        void Update()
        {
            if (graph == null || !graph.Active || graph.IsVehicleDestroyed)
            {
                Destroy(gameObject);
                return;
            }
            float elapsed = Time.unscaledTime - startedAt;
            if (whileDestructionRoundArmed)
            {
                if (!PlayerSkillCombatEffects.IsDestructionRoundArmed(
                        graph.transform))
                {
                    Destroy(gameObject);
                    return;
                }
            }
            else if (elapsed >= duration)
            {
                Destroy(gameObject);
                return;
            }

            float fade = whileDestructionRoundArmed
                ? Mathf.Clamp01(elapsed / 0.18f)
                : Mathf.Min(
                    Mathf.Clamp01(elapsed / 0.18f),
                    Mathf.Clamp01((duration - elapsed) / 0.35f));
            Color visible = tint;
            visible.a *= fade;
            SkillVfxMaterialUtility.SetTint(auraMaterial, visible);
            SkillVfxMaterialUtility.SetTint(ribbonMaterial, visible);

            Camera camera = Camera.main;
            Vector3 center = graph.transform.TransformPoint(graphLocalCenter);
            for (int index = anchors.Count - 1; index >= 0; index--)
            {
                AnchorLayer anchor = anchors[index];
                if (anchor.Target == null || anchor.Aura == null)
                    continue;
                Vector3 position = anchor.Target == graph.transform
                    ? center + graph.transform.forward * auraSize * 1.8f
                    : anchor.Target.position;
                anchor.Aura.transform.position = position;
                FaceCameraWithRoll(
                    anchor.Aura.transform,
                    camera,
                    elapsed * 150f * anchor.SpinDirection);
                float pulse = 0.82f +
                              Mathf.Sin(elapsed * 8f + index) * 0.13f;
                anchor.Aura.transform.localScale =
                    Vector3.one * auraSize * pulse;
            }

            for (int index = 0; index < flows.Count; index++)
            {
                FlowLayer flow = flows[index];
                if (flow.Ribbon == null ||
                    flow.AnchorIndex >= anchors.Count)
                    continue;
                AnchorLayer anchor = anchors[flow.AnchorIndex];
                if (anchor.Target == null)
                    continue;
                Vector3 destination = anchor.Target == graph.transform
                    ? center + graph.transform.forward * auraSize * 1.8f
                    : anchor.Target.position;
                float travel = Mathf.Repeat(
                    elapsed * 1.7f + flow.Phase,
                    1f);
                float eased = travel * travel * (3f - 2f * travel);
                Vector3 arc = graph.transform.up *
                              Mathf.Sin(travel * Mathf.PI) *
                              Mathf.Max(0.6f, auraSize * 0.7f);
                Vector3 position = Vector3.Lerp(
                    center,
                    destination,
                    eased) + arc;
                flow.Ribbon.transform.position = position;
                Vector3 direction = destination - center;
                OrientRibbon(
                    flow.Ribbon.transform,
                    camera,
                    direction);
                float length = Mathf.Clamp(
                    direction.magnitude * 0.13f,
                    0.7f,
                    4.8f);
                float width = Mathf.Lerp(0.08f, 0.32f,
                    Mathf.Sin(travel * Mathf.PI));
                flow.Ribbon.transform.localScale = new Vector3(
                    length,
                    width,
                    1f);
            }
        }

        static void FaceCameraWithRoll(
            Transform item,
            Camera camera,
            float angle)
        {
            if (item == null || camera == null)
                return;
            Vector3 direction = item.position - camera.transform.position;
            if (direction.sqrMagnitude < 0.001f)
                return;
            item.rotation = Quaternion.LookRotation(
                direction.normalized,
                camera.transform.up) *
                Quaternion.AngleAxis(angle, Vector3.forward);
        }

        static void OrientRibbon(
            Transform item,
            Camera camera,
            Vector3 direction)
        {
            if (item == null || camera == null ||
                direction.sqrMagnitude < 0.001f)
                return;
            Vector3 facing = item.position - camera.transform.position;
            if (facing.sqrMagnitude < 0.001f)
                return;
            facing.Normalize();
            Vector3 up = Vector3.Cross(facing, direction.normalized);
            if (up.sqrMagnitude < 0.001f)
                up = camera.transform.up;
            item.rotation = Quaternion.LookRotation(facing, up.normalized);
        }

        void OnDestroy()
        {
            if (auraMaterial != null)
                Destroy(auraMaterial);
            if (ribbonMaterial != null)
                Destroy(ribbonMaterial);
        }
    }

    public sealed class SkillFreezeOverlayVisual : MonoBehaviour
    {
        Transform target;
        Vector3 targetLocalCenter;
        Vector3[] localShardOffsets;
        GameObject sigil;
        readonly List<GameObject> shards = new List<GameObject>();
        Material sigilMaterial;
        Material shardMaterial;
        float startedAt;
        float duration;
        float visualSize;

        public static void Spawn(Transform target, float seconds)
        {
            if (target == null)
                return;
            GameObject root = new GameObject("SkillFreezeOverlay");
            SkillFreezeOverlayVisual visual =
                root.AddComponent<SkillFreezeOverlayVisual>();
            visual.Initialize(target, seconds);
        }

        void Initialize(Transform owner, float seconds)
        {
            target = owner;
            duration = Mathf.Max(0.15f, seconds);
            startedAt = Time.unscaledTime;
            Renderer[] targetRenderers =
                target.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = targetRenderers.Length > 0 &&
                            targetRenderers[0] != null
                ? targetRenderers[0].bounds
                : new Bounds(target.position, Vector3.one * 8f);
            for (int index = 1; index < targetRenderers.Length; index++)
                if (targetRenderers[index] != null)
                    bounds.Encapsulate(targetRenderers[index].bounds);
            targetLocalCenter = target.InverseTransformPoint(bounds.center);
            visualSize = Mathf.Clamp(
                bounds.extents.magnitude * 1.25f,
                5f,
                18f);
            localShardOffsets = new[]
            {
                target.InverseTransformVector(
                    -target.right * bounds.extents.x * 0.55f -
                    target.up * bounds.extents.y * 0.35f),
                target.InverseTransformVector(
                    target.right * bounds.extents.x * 0.52f -
                    target.up * bounds.extents.y * 0.28f),
                target.InverseTransformVector(
                    target.forward * bounds.extents.z * 0.42f -
                    target.up * bounds.extents.y * 0.48f)
            };
            sigilMaterial = SkillVfxMaterialUtility.Create(
                "AbsoluteFreezeSigil",
                new Color(0.16f, 0.82f, 1f, 0.62f),
                1.25f);
            shardMaterial = SkillVfxMaterialUtility.Create(
                "IceShardCluster",
                new Color(0.42f, 0.92f, 1f, 0.86f),
                1.1f);
            sigil = SkillVfxMaterialUtility.CreateQuad(
                "FreezeTimeLock",
                transform,
                sigilMaterial);
            for (int index = 0; index < localShardOffsets.Length; index++)
                shards.Add(SkillVfxMaterialUtility.CreateQuad(
                    "FreezeCrystal_" + index,
                    transform,
                    shardMaterial));
        }

        void Update()
        {
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                Destroy(gameObject);
                return;
            }
            float elapsed = Time.unscaledTime - startedAt;
            if (elapsed >= duration)
            {
                Destroy(gameObject);
                return;
            }
            float progress = Mathf.Clamp01(elapsed / duration);
            float growth = Mathf.Clamp01(elapsed / 0.32f);
            growth = growth * growth * (3f - 2f * growth);
            float fade = Mathf.Min(
                growth,
                Mathf.Clamp01((duration - elapsed) / 0.28f));
            Vector3 center = target.TransformPoint(targetLocalCenter);
            Camera camera = Camera.main;
            sigil.transform.position = center;
            FaceCamera(
                sigil.transform,
                camera,
                elapsed * 34f);
            sigil.transform.localScale = Vector3.one *
                                         visualSize *
                                         (0.82f + progress * 0.18f);
            for (int index = 0; index < shards.Count; index++)
            {
                GameObject shard = shards[index];
                if (shard == null)
                    continue;
                shard.transform.position = center +
                    target.TransformVector(localShardOffsets[index]);
                FaceCamera(
                    shard.transform,
                    camera,
                    (index - 1) * 17f);
                float size = visualSize * (0.26f + index * 0.035f);
                shard.transform.localScale = new Vector3(
                    size * growth,
                    size * 1.35f * growth,
                    1f);
            }
            Color sigilTint = new Color(0.16f, 0.82f, 1f, 0.62f * fade);
            Color shardTint = new Color(0.42f, 0.92f, 1f, 0.86f * fade);
            SkillVfxMaterialUtility.SetTint(sigilMaterial, sigilTint);
            SkillVfxMaterialUtility.SetTint(shardMaterial, shardTint);
        }

        static void FaceCamera(
            Transform item,
            Camera camera,
            float roll)
        {
            if (item == null || camera == null)
                return;
            Vector3 direction = item.position - camera.transform.position;
            if (direction.sqrMagnitude < 0.001f)
                return;
            item.rotation = Quaternion.LookRotation(
                direction.normalized,
                camera.transform.up) *
                Quaternion.AngleAxis(roll, Vector3.forward);
        }

        void OnDestroy()
        {
            if (sigilMaterial != null)
                Destroy(sigilMaterial);
            if (shardMaterial != null)
                Destroy(shardMaterial);
        }
    }

    public sealed class DroneMaterializeVisual : MonoBehaviour
    {
        Vector3 finalLocalScale;
        Vector3 finalLocalPosition;
        float startedAt;
        float duration;
        bool completed;

        public static void Attach(Transform visual, float seconds)
        {
            if (visual == null)
                return;
            DroneMaterializeVisual effect =
                visual.GetComponent<DroneMaterializeVisual>() ??
                visual.gameObject.AddComponent<DroneMaterializeVisual>();
            effect.Begin(seconds);
        }

        void Begin(float seconds)
        {
            finalLocalScale = transform.localScale;
            finalLocalPosition = transform.localPosition;
            duration = Mathf.Max(0.12f, seconds);
            startedAt = Time.unscaledTime;
            completed = false;
            transform.localScale = finalLocalScale * 0.42f;
            transform.localPosition = finalLocalPosition - Vector3.up * 1.2f;
        }

        void Update()
        {
            float progress = Mathf.Clamp01(
                (Time.unscaledTime - startedAt) / duration);
            float smooth = progress * progress * (3f - 2f * progress);
            transform.localScale = Vector3.Lerp(
                finalLocalScale * 0.42f,
                finalLocalScale,
                smooth);
            transform.localPosition = Vector3.Lerp(
                finalLocalPosition - Vector3.up * 1.2f,
                finalLocalPosition,
                smooth);
            if (progress < 1f)
                return;
            completed = true;
            Destroy(this);
        }

        void OnDestroy()
        {
            if (!completed && transform != null)
            {
                transform.localScale = finalLocalScale;
                transform.localPosition = finalLocalPosition;
            }
        }
    }

    public sealed class SkillBeamVisual : MonoBehaviour
    {
        LineRenderer line;
        Material material;
        float startedAt;
        float duration;
        float width;

        public static void Spawn(
            Vector3 start,
            Vector3 end,
            Color color,
            float lineWidth,
            float seconds)
        {
            GameObject root = new GameObject("SkillBeamVisual");
            SkillBeamVisual visual = root.AddComponent<SkillBeamVisual>();
            visual.Initialize(start, end, color, lineWidth, seconds);
        }

        void Initialize(
            Vector3 start,
            Vector3 end,
            Color color,
            float lineWidth,
            float seconds)
        {
            startedAt = Time.unscaledTime;
            duration = Mathf.Max(0.05f, seconds);
            width = Mathf.Max(0.01f, lineWidth);
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.widthMultiplier = width;
            line.numCapVertices = 4;
            line.startColor = color;
            line.endColor = Color.white;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                material = new Material(shader);
                line.sharedMaterial = material;
            }
        }

        void Update()
        {
            float progress = Mathf.Clamp01(
                (Time.unscaledTime - startedAt) / duration);
            if (progress >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            line.widthMultiplier = Mathf.Lerp(width, 0f, progress);
        }

        void OnDestroy()
        {
            if (material != null)
                Destroy(material);
        }
    }

    public sealed class SkillRingVisual : MonoBehaviour
    {
        readonly List<LineRenderer> rings = new List<LineRenderer>();
        readonly List<Material> materials = new List<Material>();
        float startedAt;
        float duration;
        float radius;

        public static void SpawnAttached(
            Transform parent,
            float radius,
            Color color,
            float duration,
            int count)
        {
            GameObject root = new GameObject("SkillRingVisual");
            root.transform.SetParent(parent, false);
            SkillRingVisual visual = root.AddComponent<SkillRingVisual>();
            visual.Initialize(radius, color, duration, count);
        }

        public static void SpawnWorld(
            Vector3 position,
            float radius,
            Color color,
            float duration,
            int count)
        {
            GameObject root = new GameObject("SkillRingVisual");
            root.transform.position = position;
            SkillRingVisual visual = root.AddComponent<SkillRingVisual>();
            visual.Initialize(radius, color, duration, count);
        }

        void Initialize(
            float targetRadius,
            Color color,
            float seconds,
            int count)
        {
            radius = Mathf.Max(0.25f, targetRadius);
            duration = Mathf.Max(0.1f, seconds);
            startedAt = Time.unscaledTime;
            count = Mathf.Clamp(count, 1, 6);
            Shader shader = Shader.Find("Sprites/Default");
            for (int ringIndex = 0; ringIndex < count; ringIndex++)
            {
                GameObject ringRoot = new GameObject("Ring_" + ringIndex);
                ringRoot.transform.SetParent(transform, false);
                ringRoot.transform.localRotation = Quaternion.Euler(
                    ringIndex * 32f,
                    ringIndex * 19f,
                    0f);
                LineRenderer line = ringRoot.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = true;
                line.positionCount = 64;
                line.widthMultiplier = Mathf.Max(0.04f, radius * 0.014f);
                line.startColor = color;
                line.endColor = color;
                if (shader != null)
                {
                    Material material = new Material(shader);
                    line.sharedMaterial = material;
                    materials.Add(material);
                }
                for (int point = 0; point < 64; point++)
                {
                    float angle = point / 64f * Mathf.PI * 2f;
                    line.SetPosition(point, new Vector3(
                        Mathf.Cos(angle) * radius,
                        0f,
                        Mathf.Sin(angle) * radius));
                }
                rings.Add(line);
            }
        }

        void Update()
        {
            float elapsed = Time.unscaledTime - startedAt;
            if (elapsed >= duration)
            {
                Destroy(gameObject);
                return;
            }
            transform.Rotate(
                new Vector3(13f, 31f, 17f) * Time.unscaledDeltaTime,
                Space.Self);
            float fade = Mathf.Clamp01((duration - elapsed) / 0.45f);
            foreach (LineRenderer ring in rings)
            {
                if (ring == null)
                    continue;
                Color color = ring.startColor;
                color.a = fade;
                ring.startColor = color;
                ring.endColor = color;
            }
        }

        void OnDestroy()
        {
            foreach (Material material in materials)
                if (material != null)
                    Destroy(material);
        }
    }
}
