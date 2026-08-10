using System.Collections;
using ModularAssembly;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UnityPlanet.ModularAssembly
{
    [DisallowMultipleComponent]
    public sealed class ModularSpacecraftPauseMenu : MonoBehaviour
    {
        const int SortingOrder = 420;

        ModularAssemblyLabController controller;
        CombatTestController combat;
        Canvas pauseCanvas;
        GraphicRaycaster pauseRaycaster;
        GameObject pauseRoot;
        Text contextText;
        Text returnLabel;
        bool menuOpen;
        float previousTimeScale = 1f;
        CursorLockMode previousCursorLock;
        bool previousCursorVisible;
        bool previousAudioPause;

        public static bool IsOpen { get; private set; }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!ModularLabSceneProfile.AllowsBuildExperience(
                    SceneManager.GetActiveScene()) ||
                FindObjectOfType<ModularSpacecraftPauseMenu>() != null)
            {
                return;
            }

            new GameObject("飞船暂停界面")
                .AddComponent<ModularSpacecraftPauseMenu>();
        }

        void Awake()
        {
            BuildInterface();
            StartCoroutine(ResolveControllers());
        }

        IEnumerator ResolveControllers()
        {
            while (controller == null)
            {
                controller = FindObjectOfType<ModularAssemblyLabController>();
                combat = FindObjectOfType<CombatTestController>();
                if (controller == null)
                    yield return null;
            }
        }

        void Update()
        {
            if (menuOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                    CloseMenu();
                return;
            }

            if (controller == null)
                controller = FindObjectOfType<ModularAssemblyLabController>();
            if (combat == null)
                combat = FindObjectOfType<CombatTestController>();
            if (!ShouldOpenForEscape(
                    Input.GetKeyDown(KeyCode.Escape),
                    controller != null && controller.IsFlying,
                    combat != null && combat.IsResolving,
                    ArcadeFlightRuntimeTuningOverlay.IsInputCaptured))
            {
                return;
            }

            OpenMenu();
        }

        void OnDisable()
        {
            if (menuOpen)
                RestoreGameState();
        }

        void OnDestroy()
        {
            if (menuOpen)
                RestoreGameState();
        }

        public static bool ShouldOpenForEscape(
            bool escapePressed,
            bool isFlying,
            bool combatResolving,
            bool anotherOverlayHasInput)
        {
            return escapePressed && isFlying && !combatResolving &&
                   !anotherOverlayHasInput;
        }

        public void OpenMenu()
        {
            if (menuOpen || controller == null || !controller.IsFlying)
                return;

            menuOpen = true;
            IsOpen = true;
            previousTimeScale = Time.timeScale > 0.001f
                ? Time.timeScale
                : 1f;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            previousAudioPause = AudioListener.pause;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            pauseCanvas.enabled = true;
            pauseRaycaster.enabled = true;
            pauseRoot.SetActive(true);

            bool inCombat = combat != null &&
                            (combat.IsCombatActive ||
                             combat.SessionKind ==
                             GridFlightSessionKind.CombatTest);
            contextText.text = inCombat
                ? "城市空战已暂停\n风场、磁场和敌机均已停止推进"
                : "飞船飞行已暂停\n按 ESC 或点击按钮继续";
            returnLabel.text = inCombat
                ? "结束战斗并返回改装"
                : "结束飞行并返回改装";
        }

        public void CloseMenu()
        {
            if (!menuOpen)
                return;
            RestoreGameState();
        }

        void ReturnToAssembly()
        {
            if (!menuOpen)
                return;

            bool inCombat = combat != null &&
                            (combat.IsCombatActive ||
                             combat.SessionKind ==
                             GridFlightSessionKind.CombatTest);
            RestoreGameState();
            if (inCombat)
                combat.RequestExitCombat();
            else if (controller != null && controller.IsFlying)
                controller.ToggleFlight();
        }

        void RestoreGameState()
        {
            menuOpen = false;
            IsOpen = false;
            Time.timeScale = previousTimeScale > 0.001f
                ? previousTimeScale
                : 1f;
            AudioListener.pause = previousAudioPause;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            if (pauseRoot != null)
                pauseRoot.SetActive(false);
        }

        void BuildInterface()
        {
            Font font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            GameObject canvasObject = new GameObject(
                "飞船暂停画布",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            pauseCanvas = canvasObject.GetComponent<Canvas>();
            pauseCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            pauseCanvas.sortingOrder = SortingOrder;
            pauseRaycaster = canvasObject.GetComponent<GraphicRaycaster>();
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            pauseRoot = new GameObject(
                "暂停遮罩",
                typeof(RectTransform),
                typeof(Image));
            pauseRoot.transform.SetParent(canvasObject.transform, false);
            RectTransform mask = pauseRoot.GetComponent<RectTransform>();
            mask.anchorMin = Vector2.zero;
            mask.anchorMax = Vector2.one;
            mask.offsetMin = Vector2.zero;
            mask.offsetMax = Vector2.zero;
            pauseRoot.GetComponent<Image>().color =
                new Color(0.005f, 0.012f, 0.025f, 0.76f);

            GameObject panel = new GameObject(
                "暂停面板",
                typeof(RectTransform),
                typeof(Image));
            panel.transform.SetParent(pauseRoot.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax =
                new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(620f, 430f);
            panel.GetComponent<Image>().color =
                new Color(0.025f, 0.07f, 0.11f, 0.98f);

            Text title = CreateText(
                panel.transform,
                "飞船暂停",
                font,
                38,
                new Vector2(0f, 136f),
                new Vector2(520f, 64f));
            title.color = new Color(0.30f, 0.88f, 1f, 1f);
            contextText = CreateText(
                panel.transform,
                string.Empty,
                font,
                22,
                new Vector2(0f, 58f),
                new Vector2(520f, 88f));
            contextText.color = new Color(0.78f, 0.9f, 0.96f, 1f);

            CreateButton(
                panel.transform,
                "继续飞行",
                font,
                new Vector2(0f, -40f),
                new Color(0.08f, 0.48f, 0.62f, 1f),
                CloseMenu);
            Button returnButton = CreateButton(
                panel.transform,
                "结束飞行并返回改装",
                font,
                new Vector2(0f, -118f),
                new Color(0.34f, 0.20f, 0.10f, 1f),
                ReturnToAssembly);
            returnLabel = returnButton.GetComponentInChildren<Text>(true);

            Text hint = CreateText(
                panel.transform,
                "ESC：继续    飞行输入在此界面打开时不会生效",
                font,
                17,
                new Vector2(0f, -180f),
                new Vector2(520f, 36f));
            hint.color = new Color(0.48f, 0.62f, 0.70f, 1f);
            pauseRoot.SetActive(false);
        }

        static Text CreateText(
            Transform parent,
            string value,
            Font font,
            int size,
            Vector2 position,
            Vector2 dimensions)
        {
            GameObject textObject = new GameObject(
                "文字",
                typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            Text text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.text = value;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        static Button CreateButton(
            Transform parent,
            string label,
            Font font,
            Vector2 position,
            Color color,
            UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = new GameObject(
                label,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(430f, 58f);
            buttonObject.GetComponent<Image>().color = color;
            Button button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(action);
            Text text = CreateText(
                buttonObject.transform,
                label,
                font,
                22,
                Vector2.zero,
                rect.sizeDelta);
            text.color = Color.white;
            return button;
        }
    }
}
