using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlanet.ModularAssembly
{
    /// <summary>
    /// Runtime-only tuning surface for the modular assembly test-flight scene.
    /// It edits the player's private runtime copy, so moving sliders cannot
    /// mutate the project asset or affect injected AI controllers.
    /// </summary>
    public sealed class ArcadeFlightRuntimeTuningOverlay : MonoBehaviour
    {
        const float PanelWidth = 590f;
        const string FieldPrefix = "ArcadeFlightRuntime.";

        public static bool IsInputCaptured { get; private set; }

        readonly Dictionary<string, string> valueBuffers =
            new Dictionary<string, string>();

        ModularAssemblyLabController controller;
        RobocraftMotionCoordinator motion;
        ArcadeFlightTuningProfile tuning;
        Rigidbody body;
        Rect windowRect;
        Vector2 scroll;
        bool panelOpen;
        CursorLockMode cursorLockBeforeOpen;
        bool cursorVisibleBeforeOpen;
        bool unsavedChanges;
        string notification;
        float notificationUntil;
        GUIStyle titleStyle;
        GUIStyle sectionStyle;
        GUIStyle labelStyle;
        GUIStyle hintStyle;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            IsInputCaptured = false;
            Scene scene = SceneManager.GetActiveScene();
            if (!ModularLabSceneProfile.AllowsBuildExperience(scene) ||
                FindObjectOfType<ArcadeFlightRuntimeTuningOverlay>() != null)
            {
                return;
            }

            new GameObject("ArcadeFlightRuntimeTuning")
                .AddComponent<ArcadeFlightRuntimeTuningOverlay>();
        }

        IEnumerator Start()
        {
            while (controller == null || motion == null)
            {
                controller = FindObjectOfType<
                    ModularAssemblyLabController>();
                motion = FindObjectOfType<
                    RobocraftMotionCoordinator>();
                yield return null;
            }

            body = motion.GetComponent<Rigidbody>();
            tuning = motion.RuntimeArcadeFlightTuning;
            RepositionWindow();
        }

        void Update()
        {
            if (controller == null || motion == null)
                return;

            if (ModularSpacecraftPauseMenu.IsOpen)
                return;

            if (!controller.IsFlying)
            {
                if (panelOpen)
                    ClosePanel(false);
                return;
            }

            if (Input.GetKeyDown(KeyCode.F6))
            {
                if (panelOpen)
                    ClosePanel(true);
                else
                    OpenPanel();
                return;
            }

            if (panelOpen && Input.GetKeyDown(KeyCode.Escape))
                ClosePanel(true);
        }

        void OnDisable()
        {
            if (panelOpen)
                ClosePanel(controller != null && controller.IsFlying);
            IsInputCaptured = false;
        }

        void OnGUI()
        {
            if (controller == null || motion == null ||
                !controller.IsFlying ||
                ModularSpacecraftPauseMenu.IsOpen)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -500;
            if (!panelOpen)
            {
                Rect buttonRect = new Rect(
                    Mathf.Max(12f, Screen.width - 226f),
                    18f,
                    208f,
                    42f);
                if (GUI.Button(buttonRect, "飞行手感调节  F6"))
                    OpenPanel();
                return;
            }

            float width = Mathf.Min(PanelWidth, Screen.width - 24f);
            float height = Mathf.Max(360f, Screen.height - 24f);
            windowRect.width = width;
            windowRect.height = height;
            windowRect.x = Mathf.Clamp(
                windowRect.x,
                0f,
                Mathf.Max(0f, Screen.width - width));
            windowRect.y = Mathf.Clamp(
                windowRect.y,
                0f,
                Mathf.Max(0f, Screen.height - height));
            windowRect = GUI.Window(
                GetInstanceID(),
                windowRect,
                DrawWindow,
                "街机飞行实时调节");
        }

        void DrawWindow(int id)
        {
            GUILayout.Space(4f);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("玩家街机飞行手感", titleStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("关闭 F6", GUILayout.Width(92f)))
                {
                    ClosePanel(true);
                    return;
                }
            }

            DrawFlightStatus();
            scroll = GUILayout.BeginScrollView(
                scroll,
                false,
                true,
                GUILayout.Height(Mathf.Max(220f, windowRect.height - 172f)));

            DrawSection("输入响应");
            DrawSlider(
                "deadzone",
                "移动输入死区",
                ref tuning.movementDeadzone,
                0f,
                0.3f,
                "0.000");
            DrawSlider(
                "inputCurve",
                "输入幅度曲线",
                ref tuning.inputResponseExponent,
                0.25f,
                3f,
                "0.00");

            DrawSection("速度与推进改装");
            DrawSlider(
                "baseSpeed",
                "基础目标速度",
                ref tuning.baseTargetSpeed,
                0f,
                120f,
                "0.0");
            DrawSlider(
                "minSpeed",
                "最低目标速度",
                ref tuning.minimumTargetSpeed,
                0f,
                120f,
                "0.0");
            DrawSlider(
                "maxSpeed",
                "最高目标速度",
                ref tuning.maximumTargetSpeed,
                1f,
                250f,
                "0.0");
            DrawSlider(
                "accelerationSpeed",
                "推进改装速度收益",
                ref tuning.accelerationToSpeed,
                0f,
                10f,
                "0.00");
            DrawSlider(
                "boost",
                "Boost速度倍率",
                ref tuning.boostSpeedMultiplier,
                1f,
                3f,
                "0.00");

            DrawSection("变向与侧滑");
            DrawSlider(
                "intentResponse",
                "新方向响应时间（秒）",
                ref tuning.intentResponseSeconds,
                0.03f,
                1f,
                "0.000");
            DrawSlider(
                "driftResponse",
                "旧惯性消除时间（秒）",
                ref tuning.driftResponseSeconds,
                0.03f,
                3f,
                "0.000");
            DrawSlider(
                "intentAuthority",
                "新方向权限占比",
                ref tuning.intentAuthorityFraction,
                0.1f,
                1f,
                "0.00");
            DrawSlider(
                "driftAuthority",
                "消除侧滑权限上限",
                ref tuning.driftAuthorityFraction,
                0f,
                1f,
                "0.00");

            DrawSection("松手急停与定点");
            DrawSlider(
                "stopGain",
                "急停反馈强度",
                ref tuning.stopVelocityGain,
                1f,
                30f,
                "0.0");
            DrawSlider(
                "stopAuthority",
                "急停权限占比",
                ref tuning.stopAuthorityFraction,
                0.1f,
                1f,
                "0.00");
            DrawSlider(
                "holdGain",
                "返回松键位置强度",
                ref tuning.positionHoldGain,
                0f,
                20f,
                "0.0");
            DrawSlider(
                "holdAuthority",
                "定点权限占比",
                ref tuning.positionHoldAuthorityFraction,
                0f,
                1f,
                "0.00");

            DrawSection("重力与姿态");
            DrawSlider(
                "movingGravity",
                "移动时重力补偿",
                ref tuning.movingGravitySupport,
                0f,
                2f,
                "0.00");
            DrawSlider(
                "idleGravity",
                "松键时重力补偿",
                ref tuning.idleGravitySupport,
                0f,
                2f,
                "0.00");
            DrawSlider(
                "aimTorque",
                "瞄准转向力度",
                ref tuning.aimTorqueMultiplier,
                0.1f,
                2f,
                "0.00");
            DrawSlider(
                "strafeRoll",
                "横移自动侧倾",
                ref tuning.strafeRollCoupling,
                0f,
                0.8f,
                "0.00");

            GUILayout.Space(10f);
            GUILayout.Label("快速预设", sectionStyle);
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("默认均衡"))
                    ApplyPreset(ArcadeFlightTuningPreset.Balanced);
                if (GUILayout.Button("更灵敏"))
                    ApplyPreset(ArcadeFlightTuningPreset.Responsive);
                if (GUILayout.Button("更多惯性"))
                    ApplyPreset(ArcadeFlightTuningPreset.Drifty);
            }

            GUILayout.Space(8f);
            GUILayout.Label(
                "滑块修改会立刻作用于当前试飞；保存后下次试飞继续使用。项目默认值仍可在Unity编辑器工具中修改。",
                hintStyle);
            GUILayout.EndScrollView();

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("保存为本机试飞参数", GUILayout.Height(34f)))
                    SaveRuntimeSettings();
                if (GUILayout.Button("恢复项目默认", GUILayout.Height(34f)))
                    RestoreProjectDefaults();
            }
            if (!string.IsNullOrEmpty(notification) &&
                Time.unscaledTime < notificationUntil)
            {
                GUILayout.Label(notification, hintStyle);
            }
            else if (unsavedChanges)
            {
                GUILayout.Label("当前修改尚未保存", hintStyle);
            }

            GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 34f));
        }

        void DrawFlightStatus()
        {
            float speed = body != null ? body.velocity.magnitude : 0f;
            RobocraftTelemetry telemetry = motion.Telemetry;
            bool arcade = motion.CoreAssistMode ==
                          VehicleCoreAssistMode.Training;
            GUILayout.Label(
                $"模式：{(arcade ? "街机" : "标准（参数暂不生效）")}   " +
                $"速度：{speed:0.0} m/s   质量：{telemetry.totalMass:0} kg",
                labelStyle);
            if (!arcade && GUILayout.Button("切换到街机模式"))
                motion.SetCoreAssistMode(VehicleCoreAssistMode.Training);
        }

        void DrawSection(string title)
        {
            GUILayout.Space(8f);
            GUILayout.Label(title, sectionStyle);
        }

        void DrawSlider(
            string id,
            string label,
            ref float value,
            float minimum,
            float maximum,
            string format)
        {
            GUILayout.Label(label, labelStyle);
            using (new GUILayout.HorizontalScope())
            {
                float previous = value;
                float sliderValue = GUILayout.HorizontalSlider(
                    value,
                    minimum,
                    maximum,
                    GUILayout.MinWidth(260f));
                string controlName = FieldPrefix + id;
                bool focused = GUI.GetNameOfFocusedControl() == controlName;
                if (!valueBuffers.TryGetValue(id, out string buffer) ||
                    !focused)
                {
                    buffer = value.ToString(
                        format,
                        CultureInfo.InvariantCulture);
                }
                GUI.SetNextControlName(controlName);
                string entered = GUILayout.TextField(
                    buffer,
                    GUILayout.Width(82f));

                if (!Mathf.Approximately(sliderValue, previous))
                {
                    value = sliderValue;
                    entered = value.ToString(
                        format,
                        CultureInfo.InvariantCulture);
                }
                else if (TryParseFloat(entered, out float parsed))
                {
                    value = Mathf.Clamp(parsed, minimum, maximum);
                }
                valueBuffers[id] = entered;
                if (!Mathf.Approximately(value, previous))
                    unsavedChanges = true;
            }
        }

        static bool TryParseFloat(string value, out float result)
        {
            return float.TryParse(
                       value,
                       NumberStyles.Float,
                       CultureInfo.CurrentCulture,
                       out result) ||
                   float.TryParse(
                       value,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out result);
        }

        void ApplyPreset(ArcadeFlightTuningPreset preset)
        {
            tuning.ApplyPreset(preset);
            valueBuffers.Clear();
            unsavedChanges = true;
            Notify("预设已即时应用");
        }

        void SaveRuntimeSettings()
        {
            tuning.SaveRuntimeOverrides();
            unsavedChanges = false;
            Notify("本机试飞参数已保存");
        }

        void RestoreProjectDefaults()
        {
            ArcadeFlightTuningProfile.ClearRuntimeOverrides();
            motion.ReloadArcadeFlightTuning(false);
            tuning = motion.RuntimeArcadeFlightTuning;
            valueBuffers.Clear();
            unsavedChanges = false;
            Notify("已恢复项目默认参数");
        }

        void Notify(string message)
        {
            notification = message;
            notificationUntil = Time.unscaledTime + 2.5f;
        }

        void OpenPanel()
        {
            if (panelOpen || motion == null)
                return;
            tuning = motion.RuntimeArcadeFlightTuning;
            body = motion.GetComponent<Rigidbody>();
            cursorLockBeforeOpen = Cursor.lockState;
            cursorVisibleBeforeOpen = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            panelOpen = true;
            IsInputCaptured = true;
            valueBuffers.Clear();
            RepositionWindow();
        }

        void ClosePanel(bool restoreFlightCursor)
        {
            if (!panelOpen)
                return;
            panelOpen = false;
            IsInputCaptured = false;
            if (restoreFlightCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = cursorLockBeforeOpen;
                Cursor.visible = cursorVisibleBeforeOpen;
            }
            GUI.FocusControl(null);
        }

        void RepositionWindow()
        {
            float width = Mathf.Min(PanelWidth, Screen.width - 24f);
            windowRect = new Rect(
                Mathf.Max(12f, Screen.width - width - 12f),
                12f,
                width,
                Mathf.Max(360f, Screen.height - 24f));
        }

        void EnsureStyles()
        {
            if (titleStyle != null)
                return;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.25f, 1f, 0.86f) }
            };
            sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.48f, 0.88f, 1f) }
            };
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                wordWrap = true
            };
            hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                normal = { textColor = new Color(0.72f, 0.82f, 0.86f) }
            };
        }
    }
}
