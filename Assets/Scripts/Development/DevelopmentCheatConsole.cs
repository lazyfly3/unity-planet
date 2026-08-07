using System;
using UnityEngine;
using UnityPlanet.SpaceStation.Enhancement;
using UnityPlanet.SpaceStation.Skills;

namespace UnityPlanet.Development
{
    [DisallowMultipleComponent]
    public sealed class DevelopmentCheatConsole : MonoBehaviour
    {
        const string InputControlName = "DevelopmentCheatCodeInput";
        const int GalaxyCoinCheatBalance = 9999;

        static DevelopmentCheatConsole instance;

        bool open;
        bool focusInput;
        string input = string.Empty;
        string feedback = string.Empty;
        Color feedbackColor = Color.white;
        CursorLockMode previousCursorLock;
        bool previousCursorVisible;
        GUIStyle titleStyle;
        GUIStyle hintStyle;
        GUIStyle feedbackStyle;
        GUIStyle inputStyle;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureRuntimeConsole()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (instance != null)
                return;
            DevelopmentCheatConsole existing =
                FindObjectOfType<DevelopmentCheatConsole>();
            if (existing != null)
            {
                instance = existing;
                DontDestroyOnLoad(existing.gameObject);
                return;
            }
            GameObject root = new GameObject("DevelopmentCheatConsole");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<DevelopmentCheatConsole>();
#endif
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.BackQuote))
                SetOpen(!open);
        }

        void OnDestroy()
        {
            if (instance == this)
                instance = null;
            if (open)
                RestoreCursor();
        }

        void SetOpen(bool value)
        {
            if (open == value)
                return;
            open = value;
            input = string.Empty;
            feedback = value
                ? "可用作弊码：jn 解锁全部技能，jb 获得 9999 银河币，lq 技能无冷却"
                : string.Empty;
            feedbackColor = new Color(0.45f, 0.9f, 1f);
            if (open)
            {
                previousCursorLock = Cursor.lockState;
                previousCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                focusInput = true;
            }
            else
                RestoreCursor();
        }

        void RestoreCursor()
        {
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        void OnGUI()
        {
            if (!open)
                return;
            EnsureStyles();
            Event current = Event.current;
            float width = Mathf.Min(620f, Screen.width - 40f);
            Rect panel = new Rect(
                (Screen.width - width) * 0.5f,
                Mathf.Max(24f, Screen.height * 0.16f),
                width,
                190f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.015f, 0.055f, 0.075f, 0.97f);
            GUI.Box(panel, GUIContent.none);
            GUI.color = new Color(0.08f, 0.88f, 1f, 0.95f);
            GUI.Box(
                new Rect(panel.x, panel.y, panel.width, 3f),
                GUIContent.none);
            GUI.color = previousColor;

            GUI.Label(
                new Rect(panel.x + 24f, panel.y + 18f, panel.width - 48f, 30f),
                "测试作弊码",
                titleStyle);
            GUI.Label(
                new Rect(panel.x + 24f, panel.y + 51f, panel.width - 48f, 24f),
                "输入代码后按 Enter 执行；按 ` 或 Esc 关闭",
                hintStyle);
            GUI.SetNextControlName(InputControlName);
            input = GUI.TextField(
                new Rect(panel.x + 24f, panel.y + 82f, panel.width - 48f, 40f),
                input,
                24,
                inputStyle);
            input = input.Replace("`", string.Empty)
                         .Replace("~", string.Empty);
            feedbackStyle.normal.textColor = feedbackColor;
            GUI.Label(
                new Rect(panel.x + 24f, panel.y + 136f, panel.width - 48f, 28f),
                feedback,
                feedbackStyle);

            if (focusInput)
            {
                GUI.FocusControl(InputControlName);
                focusInput = false;
            }
            if (current.type != EventType.KeyDown)
                return;
            if (current.keyCode == KeyCode.Escape)
            {
                SetOpen(false);
                current.Use();
                return;
            }
            if (current.keyCode == KeyCode.Return ||
                current.keyCode == KeyCode.KeypadEnter)
            {
                Execute(input);
                input = string.Empty;
                focusInput = true;
                current.Use();
            }
        }

        void Execute(string rawCode)
        {
            string code = (rawCode ?? string.Empty)
                .Trim()
                .ToLowerInvariant();
            try
            {
                switch (code)
                {
                    case "jn":
                        int unlocked =
                            PlayerSkillProgressService.UnlockAllForTesting();
                        feedback = unlocked > 0
                            ? "已解锁全部技能，新解锁 " + unlocked + " 个。"
                            : "全部技能已经处于解锁状态。";
                        feedbackColor = new Color(0.25f, 1f, 0.62f);
                        break;
                    case "jb":
                        GalaxyEnhancementProgressData currency =
                            GalaxyCurrencyService.LoadOrCreate();
                        currency.galaxyCoins = GalaxyCoinCheatBalance;
                        GalaxyCurrencyService.Save(currency);
                        feedback = "银河币已设置为 9,999。";
                        feedbackColor = new Color(1f, 0.78f, 0.22f);
                        break;
                    case "lq":
                        PlayerSkillCombatEffects.SetNoCooldownForTesting(true);
                        feedback = "技能无冷却已开启，本次运行期间持续生效。";
                        feedbackColor = new Color(0.3f, 1f, 0.72f);
                        break;
                    case "":
                        feedback = "请输入作弊码。";
                        feedbackColor = new Color(1f, 0.68f, 0.3f);
                        break;
                    default:
                        feedback = "未知作弊码：" + code;
                        feedbackColor = new Color(1f, 0.35f, 0.3f);
                        break;
                }
            }
            catch (Exception exception)
            {
                feedback = "作弊码执行失败：" + exception.Message;
                feedbackColor = new Color(1f, 0.25f, 0.2f);
                Debug.LogException(exception, this);
            }
        }

        void EnsureStyles()
        {
            if (titleStyle != null)
                return;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            titleStyle.normal.textColor = new Color(0.2f, 1f, 0.95f);
            hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleLeft
            };
            hintStyle.normal.textColor = new Color(0.62f, 0.82f, 0.9f);
            feedbackStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleLeft
            };
            inputStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(12, 12, 5, 5)
            };
            inputStyle.normal.textColor = Color.white;
            inputStyle.focused.textColor = Color.white;
        }
    }
}
