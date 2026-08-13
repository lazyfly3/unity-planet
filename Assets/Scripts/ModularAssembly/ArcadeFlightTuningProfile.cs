using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    public enum ArcadeFlightTuningPreset
    {
        Balanced,
        Responsive,
        Drifty
    }

    [CreateAssetMenu(
        fileName = "ArcadeFlightTuning",
        menuName = "Unity Planet/街机飞行手感参数")]
    public sealed class ArcadeFlightTuningProfile : ScriptableObject
    {
        public const string ResourcesPath =
            "ModularAssembly/ArcadeFlightTuning";
        public const string RuntimePreferencesKey =
            "UnityPlanet.ArcadeFlightTuning.V1";

        [Header("输入")]
        [Tooltip("小于这个幅度的移动输入会被忽略。键盘通常不受影响，手柄摇杆可用它过滤漂移。")]
        [Range(0f, 0.3f)] public float movementDeadzone = 0.01f;
        [Tooltip("输入幅度到目标速度的曲线。小于1更灵敏，大于1更细腻。")]
        [Range(0.25f, 3f)] public float inputResponseExponent = 1f;

        [Header("目标速度")]
        [Min(0f)] public float baseTargetSpeed = 12f;
        [Min(0f)] public float minimumTargetSpeed = 12f;
        [Min(1f)] public float maximumTargetSpeed = 65f;
        [Tooltip("每1m/s²可用加速度换算成多少目标速度。数值越大，改装推进器对极速影响越明显。")]
        [Range(0f, 10f)] public float accelerationToSpeed = 3.5f;
        [Range(1f, 3f)] public float boostSpeedMultiplier = 1.6f;

        [Header("移动与变向")]
        [Tooltip("沿当前输入方向建立目标速度所需的响应时间。越小越灵敏。")]
        [Range(0.03f, 1f)] public float intentResponseSeconds = 0.16f;
        [Tooltip("消除旧方向侧滑所需的响应时间。它属于次级请求，不会抢占新方向输入。")]
        [Range(0.03f, 3f)] public float driftResponseSeconds = 0.16f;
        [Tooltip("新方向最多预占多少幸存推进权限。剩余权限留给转向和消除侧滑。")]
        [Range(0.1f, 1f)] public float intentAuthorityFraction = 0.75f;
        [Tooltip("消除侧滑最多可申请多少方向权限；仍只能使用新意图和姿态分配后的剩余推力。")]
        [Range(0f, 1f)] public float driftAuthorityFraction = 1f;

        [Header("下降手感")]
        [Tooltip("按住Ctrl建立下降目标速度所需的响应时间。只影响街机模式下降，不影响标准模式。")]
        [Range(0.03f, 0.5f)] public float descentResponseSeconds = 0.08f;
        [Tooltip("下降意图最多可以使用多少幸存推进权限。顶部喷气和模块损坏仍会改变最终下降能力。")]
        [Range(0.1f, 1f)] public float descentAuthorityFraction = 1f;
        [Tooltip("同时按住WASD和Ctrl时，下降输入相对平面输入的优先权。")]
        [Range(1f, 2f)] public float descentInputWeight = 1.35f;
        [Tooltip("街机模式为重型飞船提供的最低下降加速度保底。实际顶部喷气会继续叠加。")]
        [Range(0.5f, 15f)] public float minimumDescentAcceleration = 6f;

        [Header("松手急停与定点")]
        [Tooltip("松开移动键后的速度反馈强度。越大越倾向于立即用满可用制动力。")]
        [Range(1f, 30f)] public float stopVelocityGain = 10f;
        [Range(0.1f, 1f)] public float stopAuthorityFraction = 1f;
        [Tooltip("返回松键位置的强度。它排在急停和姿态控制之后。")]
        [Range(0f, 20f)] public float positionHoldGain = 5f;
        [Range(0f, 1f)] public float positionHoldAuthorityFraction = 1f;

        [Header("稳定与姿态")]
        [Tooltip("移动时的重力补偿倍率。1为完全补偿，低于1会在移动时缓慢下沉。")]
        [Range(0f, 2f)] public float movingGravitySupport = 1f;
        [Tooltip("松键急停时的重力补偿倍率。")]
        [Range(0f, 2f)] public float idleGravitySupport = 1f;
        [Tooltip("玩家街机模式的瞄准转向力矩倍率。不会改变标准模式。")]
        [Range(0.1f, 2f)] public float aimTorqueMultiplier = 1f;
        [Tooltip("A/D横移时自动附加的滚转量。0表示完全不侧倾。")]
        [Range(0f, 0.8f)] public float strafeRollCoupling = 0.22f;

        public static ArcadeFlightTuningProfile Load()
        {
            return Resources.Load<ArcadeFlightTuningProfile>(
                ResourcesPath);
        }

        public static ArcadeFlightTuningProfile CreateRuntimeCopy(
            bool applyRuntimeOverrides = true)
        {
            ArcadeFlightTuningProfile source = Load();
            ArcadeFlightTuningProfile result = source != null
                ? Instantiate(source)
                : CreateInstance<ArcadeFlightTuningProfile>();
            result.hideFlags = HideFlags.DontSave;
            if (source == null)
                result.ResetToDefaults();
            if (applyRuntimeOverrides)
                result.ApplyRuntimeOverrides();
            return result;
        }

        public void ResetToDefaults()
        {
            movementDeadzone = 0.01f;
            inputResponseExponent = 1f;
            baseTargetSpeed = 12f;
            minimumTargetSpeed = 12f;
            maximumTargetSpeed = 65f;
            accelerationToSpeed = 3.5f;
            boostSpeedMultiplier = 1.6f;
            intentResponseSeconds = 0.16f;
            driftResponseSeconds = 0.16f;
            intentAuthorityFraction = 0.75f;
            driftAuthorityFraction = 1f;
            descentResponseSeconds = 0.08f;
            descentAuthorityFraction = 1f;
            descentInputWeight = 1.35f;
            minimumDescentAcceleration = 6f;
            stopVelocityGain = 10f;
            stopAuthorityFraction = 1f;
            positionHoldGain = 5f;
            positionHoldAuthorityFraction = 1f;
            movingGravitySupport = 1f;
            idleGravitySupport = 1f;
            aimTorqueMultiplier = 1f;
            strafeRollCoupling = 0.22f;
        }

        public void ApplyPreset(ArcadeFlightTuningPreset preset)
        {
            ResetToDefaults();
            switch (preset)
            {
                case ArcadeFlightTuningPreset.Responsive:
                    intentResponseSeconds = 0.08f;
                    driftResponseSeconds = 0.22f;
                    intentAuthorityFraction = 0.82f;
                    descentResponseSeconds = 0.055f;
                    descentInputWeight = 1.45f;
                    minimumDescentAcceleration = 7f;
                    stopVelocityGain = 14f;
                    aimTorqueMultiplier = 1.2f;
                    strafeRollCoupling = 0.16f;
                    break;
                case ArcadeFlightTuningPreset.Drifty:
                    intentResponseSeconds = 0.20f;
                    driftResponseSeconds = 0.65f;
                    intentAuthorityFraction = 0.68f;
                    driftAuthorityFraction = 0.75f;
                    descentResponseSeconds = 0.11f;
                    descentAuthorityFraction = 0.9f;
                    descentInputWeight = 1.25f;
                    minimumDescentAcceleration = 5f;
                    stopVelocityGain = 6f;
                    stopAuthorityFraction = 0.8f;
                    strafeRollCoupling = 0.32f;
                    break;
            }
            Sanitize();
        }

        public void SaveRuntimeOverrides()
        {
            Sanitize();
            PlayerPrefs.SetString(
                RuntimePreferencesKey,
                JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        public bool ApplyRuntimeOverrides()
        {
            if (!PlayerPrefs.HasKey(RuntimePreferencesKey))
                return false;
            string json = PlayerPrefs.GetString(
                RuntimePreferencesKey,
                string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return false;
            JsonUtility.FromJsonOverwrite(json, this);
            Sanitize();
            return true;
        }

        public static void ClearRuntimeOverrides()
        {
            PlayerPrefs.DeleteKey(RuntimePreferencesKey);
            PlayerPrefs.Save();
        }

        void OnValidate()
        {
            Sanitize();
        }

        void Sanitize()
        {
            movementDeadzone = Mathf.Clamp(movementDeadzone, 0f, 0.3f);
            inputResponseExponent = Mathf.Clamp(
                inputResponseExponent,
                0.25f,
                3f);
            minimumTargetSpeed = Mathf.Max(0f, minimumTargetSpeed);
            maximumTargetSpeed = Mathf.Max(
                minimumTargetSpeed,
                maximumTargetSpeed);
            baseTargetSpeed = Mathf.Max(0f, baseTargetSpeed);
            accelerationToSpeed = Mathf.Clamp(accelerationToSpeed, 0f, 10f);
            boostSpeedMultiplier = Mathf.Clamp(
                boostSpeedMultiplier,
                1f,
                3f);
            intentResponseSeconds = Mathf.Clamp(
                intentResponseSeconds,
                0.03f,
                1f);
            driftResponseSeconds = Mathf.Clamp(
                driftResponseSeconds,
                0.03f,
                3f);
            intentAuthorityFraction = Mathf.Clamp(
                intentAuthorityFraction,
                0.1f,
                1f);
            driftAuthorityFraction = Mathf.Clamp01(
                driftAuthorityFraction);
            descentResponseSeconds = Mathf.Clamp(
                descentResponseSeconds,
                0.03f,
                0.5f);
            descentAuthorityFraction = Mathf.Clamp(
                descentAuthorityFraction,
                0.1f,
                1f);
            descentInputWeight = Mathf.Clamp(
                descentInputWeight,
                1f,
                2f);
            minimumDescentAcceleration = Mathf.Clamp(
                minimumDescentAcceleration,
                0.5f,
                15f);
            stopVelocityGain = Mathf.Clamp(stopVelocityGain, 1f, 30f);
            stopAuthorityFraction = Mathf.Clamp(
                stopAuthorityFraction,
                0.1f,
                1f);
            positionHoldGain = Mathf.Clamp(positionHoldGain, 0f, 20f);
            positionHoldAuthorityFraction = Mathf.Clamp01(
                positionHoldAuthorityFraction);
            movingGravitySupport = Mathf.Clamp(
                movingGravitySupport,
                0f,
                2f);
            idleGravitySupport = Mathf.Clamp(
                idleGravitySupport,
                0f,
                2f);
            aimTorqueMultiplier = Mathf.Clamp(
                aimTorqueMultiplier,
                0.1f,
                2f);
            strafeRollCoupling = Mathf.Clamp(
                strafeRollCoupling,
                0f,
                0.8f);
        }
    }
}
