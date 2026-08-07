using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ModularAssembly;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UnityPlanet.ModularAssembly
{
    public sealed class ModularPresetLibraryUi : MonoBehaviour
    {
        static Font sharedFont;

        AirBuildExperienceController airBuild;
        ModularAssemblyLabController controller;
        Canvas canvas;
        GameObject overlay;
        RectTransform listContent;
        RawImage detailPreview;
        Text detailText;
        Text statusText;
        Button loadButton;
        Button deleteButton;
        InputField nameInput;
        bool saveMode;
        string selectedId;
        string overwriteArmedName;
        string deleteArmedId;
        bool airBuildWasEnabled;
        bool controllerWasEnabled;
        bool inputPaused;
        readonly List<Texture2D> generatedPreviews =
            new List<Texture2D>();
        IReadOnlyList<ModularPresetEntry> entries =
            Array.Empty<ModularPresetEntry>();

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterSceneHook()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindObjectOfType<ModularPresetLibraryUi>() != null)
                return;
            GameObject host = new GameObject("ModularPresetLibraryUi");
            host.AddComponent<ModularPresetLibraryUi>();
        }

        IEnumerator Start()
        {
            for (int frame = 0; frame < 600; frame++)
            {
                airBuild = FindObjectOfType<AirBuildExperienceController>();
                controller =
                    FindObjectOfType<ModularAssemblyLabController>();
                if (airBuild != null && controller != null &&
                    TryHookButtons())
                    yield break;
                yield return null;
            }
            Destroy(gameObject);
        }

        void Update()
        {
            if (overlay != null &&
                Input.GetKeyDown(KeyCode.Escape))
                CloseOverlay();
        }

        bool TryHookButtons()
        {
            Button save = FindButton("保存预制");
            Button load = FindButton("载入预制");
            if (save == null || load == null)
                return false;
            canvas = load.GetComponentInParent<Canvas>();
            if (canvas == null)
                return false;
            save.onClick.RemoveAllListeners();
            save.onClick.AddListener(OpenSaveDialog);
            load.onClick.RemoveAllListeners();
            load.onClick.AddListener(OpenLibrary);
            return true;
        }

        static Button FindButton(string label)
        {
            foreach (Button button in FindObjectsOfType<Button>(true))
            {
                // The scene still contains the hidden bootstrap UI. Hook only
                // the button owned by the active Modular air-build experience;
                // otherwise the visible button can keep its legacy direct-load
                // callback and bypass this library dialog.
                if (button.GetComponentInParent<
                        AirBuildExperienceController>() == null)
                {
                    continue;
                }
                Text text = button.GetComponentInChildren<Text>(true);
                if (text != null &&
                    string.Equals(
                        text.text,
                        label,
                        StringComparison.Ordinal))
                    return button;
            }
            return null;
        }

        void OpenLibrary()
        {
            saveMode = false;
            BuildOverlay("载入预制");
            BuildLibraryBody();
            RefreshEntries();
        }

        void OpenSaveDialog()
        {
            saveMode = true;
            BuildOverlay("保存预制");
            BuildSaveBody();
        }

        void BuildOverlay(string title)
        {
            CloseOverlay();
            PauseBuildInput();
            overlay = CreatePanel(
                canvas.transform,
                "PresetModalOverlay",
                new Color(0f, 0.02f, 0.03f, 0.78f));
            RectTransform overlayRect =
                overlay.GetComponent<RectTransform>();
            Stretch(overlayRect);
            overlayRect.SetAsLastSibling();

            GameObject panel = CreatePanel(
                overlay.transform,
                "PresetModalPanel",
                new Color(0.025f, 0.085f, 0.11f, 0.99f));
            RectTransform panelRect =
                panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax =
                new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = saveMode
                ? new Vector2(560f, 290f)
                : new Vector2(920f, 570f);

            Text heading = CreateText(
                panel.transform,
                title,
                25,
                FontStyle.Bold,
                new Color(0.18f, 0.95f, 0.9f));
            SetRect(
                heading.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(24f, -18f),
                new Vector2(500f, 42f));

            Button close = CreateButton(
                panel.transform,
                "关闭",
                new Color(0.12f, 0.31f, 0.38f, 1f));
            SetRect(
                close.GetComponent<RectTransform>(),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-18f, -16f),
                new Vector2(88f, 38f));
            close.onClick.AddListener(CloseOverlay);
        }

        void BuildLibraryBody()
        {
            Transform panel = overlay.transform.GetChild(0);
            GameObject viewport = CreatePanel(
                panel,
                "PresetListViewport",
                new Color(0.015f, 0.055f, 0.075f, 0.92f));
            RectTransform viewportRect =
                viewport.GetComponent<RectTransform>();
            SetRect(
                viewportRect,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(22f, 62f),
                new Vector2(430f, 442f));
            viewport.AddComponent<RectMask2D>();

            GameObject content = new GameObject(
                "PresetListContent",
                typeof(RectTransform));
            listContent = content.GetComponent<RectTransform>();
            listContent.SetParent(viewport.transform, false);
            listContent.anchorMin = new Vector2(0f, 1f);
            listContent.anchorMax = new Vector2(1f, 1f);
            listContent.pivot = new Vector2(0.5f, 1f);
            listContent.anchoredPosition = Vector2.zero;
            listContent.sizeDelta = new Vector2(0f, 442f);

            ScrollRect scroll = viewport.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = listContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;

            GameObject detail = CreatePanel(
                panel,
                "PresetDetail",
                new Color(0.018f, 0.06f, 0.078f, 0.96f));
            RectTransform detailRect =
                detail.GetComponent<RectTransform>();
            SetRect(
                detailRect,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-22f, 62f),
                new Vector2(430f, 442f));

            detailPreview = CreateRawImage(detail.transform);
            SetRect(
                detailPreview.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -18f),
                new Vector2(382f, 212f));

            detailText = CreateText(
                detail.transform,
                "请选择一个预制。",
                17,
                FontStyle.Normal,
                Color.white);
            detailText.alignment = TextAnchor.UpperLeft;
            SetRect(
                detailText.rectTransform,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(20f, 82f),
                new Vector2(390f, 118f));

            statusText = CreateText(
                detail.transform,
                string.Empty,
                15,
                FontStyle.Normal,
                new Color(1f, 0.76f, 0.3f));
            statusText.alignment = TextAnchor.MiddleLeft;
            SetRect(
                statusText.rectTransform,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(20f, 52f),
                new Vector2(390f, 30f));

            loadButton = CreateButton(
                detail.transform,
                "载入到改装",
                new Color(0.03f, 0.7f, 0.64f, 1f));
            SetRect(
                loadButton.GetComponent<RectTransform>(),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-18f, 14f),
                new Vector2(154f, 44f));
            loadButton.onClick.AddListener(LoadSelected);

            deleteButton = CreateButton(
                detail.transform,
                "删除",
                new Color(0.55f, 0.18f, 0.12f, 1f));
            SetRect(
                deleteButton.GetComponent<RectTransform>(),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(18f, 14f),
                new Vector2(112f, 44f));
            deleteButton.onClick.AddListener(DeleteSelected);
        }

        void BuildSaveBody()
        {
            Transform panel = overlay.transform.GetChild(0);
            Text prompt = CreateText(
                panel,
                "为当前飞船输入预制名称（最多24个字符）",
                17,
                FontStyle.Normal,
                Color.white);
            SetRect(
                prompt.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -82f),
                new Vector2(500f, 34f));

            GameObject inputObject = CreatePanel(
                panel,
                "PresetNameInput",
                new Color(0.08f, 0.18f, 0.22f, 1f));
            RectTransform inputRect =
                inputObject.GetComponent<RectTransform>();
            SetRect(
                inputRect,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -124f),
                new Vector2(480f, 48f));
            nameInput = inputObject.AddComponent<InputField>();
            Text inputText = CreateText(
                inputObject.transform,
                string.Empty,
                18,
                FontStyle.Normal,
                Color.white);
            inputText.alignment = TextAnchor.MiddleLeft;
            StretchWithMargin(inputText.rectTransform, 14f);
            nameInput.textComponent = inputText;
            nameInput.characterLimit = 24;
            Text placeholder = CreateText(
                inputObject.transform,
                "例如：轻型侦察机",
                18,
                FontStyle.Italic,
                new Color(0.55f, 0.68f, 0.72f, 0.8f));
            placeholder.alignment = TextAnchor.MiddleLeft;
            StretchWithMargin(placeholder.rectTransform, 14f);
            nameInput.placeholder = placeholder;

            statusText = CreateText(
                panel,
                string.Empty,
                15,
                FontStyle.Normal,
                new Color(1f, 0.76f, 0.3f));
            statusText.alignment = TextAnchor.MiddleLeft;
            SetRect(
                statusText.rectTransform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 72f),
                new Vector2(480f, 30f));

            Button save = CreateButton(
                panel,
                "保存",
                new Color(0.03f, 0.7f, 0.64f, 1f));
            SetRect(
                save.GetComponent<RectTransform>(),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-40f, 24f),
                new Vector2(140f, 44f));
            save.onClick.AddListener(SaveNamedPreset);

            Button cancel = CreateButton(
                panel,
                "取消",
                new Color(0.12f, 0.31f, 0.38f, 1f));
            SetRect(
                cancel.GetComponent<RectTransform>(),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(40f, 24f),
                new Vector2(140f, 44f));
            cancel.onClick.AddListener(CloseOverlay);
            nameInput.ActivateInputField();
        }

        void RefreshEntries()
        {
            DestroyGeneratedPreviews();
            entries = controller.ListNamedPresets();
            foreach (Transform child in listContent)
                Destroy(child.gameObject);
            const float rowHeight = 92f;
            listContent.sizeDelta = new Vector2(
                0f,
                Mathf.Max(442f, entries.Count * rowHeight + 8f));
            for (int index = 0; index < entries.Count; index++)
            {
                ModularPresetEntry entry = entries[index];
                GameObject row = CreatePanel(
                    listContent,
                    "Preset_" + entry.id,
                    entry.isReady
                        ? new Color(0.07f, 0.19f, 0.24f, 1f)
                        : new Color(0.16f, 0.12f, 0.12f, 1f));
                RectTransform rect = row.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition =
                    new Vector2(0f, -index * rowHeight - 4f);
                rect.sizeDelta = new Vector2(-8f, rowHeight - 8f);
                Button button = row.AddComponent<Button>();
                button.interactable = true;
                button.onClick.AddListener(() => SelectEntry(entry.id));

                RawImage image = CreateRawImage(row.transform);
                SetRect(
                    image.rectTransform,
                    new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f),
                    new Vector2(8f, 0f),
                    new Vector2(122f, 68f));
                Texture2D preview =
                    ModularPresetPreviewRenderer.Create(
                        controller.Model,
                        entry.blueprint);
                generatedPreviews.Add(preview);
                image.texture = preview;

                string badge = entry.builtIn ? "内置" : "玩家";
                string ready = entry.isReady
                    ? entry.qualification != null &&
                      entry.qualification.verified
                        ? " · 无辅助已验证"
                        : string.Empty
                    : " · 资源未就绪";
                Text label = CreateText(
                    row.transform,
                    $"{entry.displayName}\n" +
                    $"{badge} · {entry.moduleCount}模块 · " +
                    $"{entry.totalMass:F0}kg{ready}",
                    15,
                    FontStyle.Bold,
                    entry.isReady
                        ? Color.white
                        : new Color(1f, 0.62f, 0.55f));
                label.alignment = TextAnchor.MiddleLeft;
                SetRect(
                    label.rectTransform,
                    new Vector2(0f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 0.5f),
                    new Vector2(142f, 0f),
                    new Vector2(-150f, -8f));
            }

            ModularPresetEntry preferred = entries
                .FirstOrDefault(entry => entry.isReady) ??
                entries.FirstOrDefault();
            SelectEntry(preferred?.id);
        }

        void SelectEntry(string id)
        {
            selectedId = id;
            deleteArmedId = null;
            ModularPresetEntry entry = entries.FirstOrDefault(item =>
                string.Equals(item.id, id, StringComparison.Ordinal));
            if (entry == null)
            {
                detailText.text = "没有可用预制。";
                detailPreview.texture = null;
                loadButton.interactable = false;
                deleteButton.interactable = false;
                return;
            }

            Texture2D preview =
                ModularPresetPreviewRenderer.Create(
                    controller.Model,
                    entry.blueprint);
            generatedPreviews.Add(preview);
            detailPreview.texture = preview;
            string assist = entry.coreAssistMode ==
                            VehicleCoreAssistMode.Disabled
                ? "无辅助"
                : entry.coreAssistMode == VehicleCoreAssistMode.Training
                    ? "街机"
                    : "标准";
            string qualification = entry.qualification?.message ??
                                   "未执行飞行校验";
            detailText.text =
                $"{entry.displayName}\n" +
                $"来源：{(entry.builtIn ? "内置只读" : "玩家预制库")}\n" +
                $"模块：{entry.moduleCount}    质量：{entry.totalMass:F0} kg\n" +
                $"核心辅助：{assist}    物理：{entry.physicsRevision}\n" +
                $"状态：{qualification}";
            statusText.text = entry.isReady
                ? "载入后返回改装界面，不会自动试飞。"
                : entry.readinessMessage;
            loadButton.interactable = entry.isReady;
            deleteButton.interactable = !entry.builtIn;
        }

        void LoadSelected()
        {
            if (string.IsNullOrEmpty(selectedId))
                return;
            if (controller.LoadNamedPresetForBuild(
                    selectedId,
                    out string message))
            {
                CloseOverlay();
                return;
            }
            statusText.text = message;
        }

        void DeleteSelected()
        {
            ModularPresetEntry entry = entries.FirstOrDefault(item =>
                string.Equals(
                    item.id,
                    selectedId,
                    StringComparison.Ordinal));
            if (entry == null || entry.builtIn)
                return;
            if (!string.Equals(
                    deleteArmedId,
                    entry.id,
                    StringComparison.Ordinal))
            {
                deleteArmedId = entry.id;
                statusText.text =
                    "再次点击“删除”确认永久删除该预制。";
                return;
            }
            if (!controller.DeleteNamedPreset(
                    entry.id,
                    out string message))
            {
                statusText.text = message;
                return;
            }
            selectedId = null;
            RefreshEntries();
        }

        void SaveNamedPreset()
        {
            string name = nameInput?.text?.Trim() ?? string.Empty;
            bool overwrite = string.Equals(
                overwriteArmedName,
                name,
                StringComparison.OrdinalIgnoreCase);
            if (controller.SaveNamedPreset(
                    name,
                    overwrite,
                    out string message))
            {
                CloseOverlay();
                return;
            }
            if (message.IndexOf(
                    "同名",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                overwriteArmedName = name;
            statusText.text = message;
        }

        void PauseBuildInput()
        {
            if (inputPaused)
                return;
            inputPaused = true;
            if (airBuild != null)
            {
                airBuildWasEnabled = airBuild.enabled;
                airBuild.enabled = false;
            }
            if (controller != null)
            {
                controllerWasEnabled = controller.enabled;
                controller.enabled = false;
            }
        }

        void ResumeBuildInput()
        {
            if (!inputPaused)
                return;
            inputPaused = false;
            if (airBuild != null)
                airBuild.enabled = airBuildWasEnabled;
            if (controller != null)
                controller.enabled = controllerWasEnabled;
        }

        void CloseOverlay()
        {
            if (overlay != null)
                Destroy(overlay);
            overlay = null;
            listContent = null;
            detailPreview = null;
            detailText = null;
            statusText = null;
            loadButton = null;
            deleteButton = null;
            nameInput = null;
            selectedId = null;
            overwriteArmedName = null;
            deleteArmedId = null;
            DestroyGeneratedPreviews();
            ResumeBuildInput();
        }

        void DestroyGeneratedPreviews()
        {
            foreach (Texture2D texture in generatedPreviews)
                if (texture != null)
                    Destroy(texture);
            generatedPreviews.Clear();
        }

        void OnDestroy()
        {
            ResumeBuildInput();
            DestroyGeneratedPreviews();
        }

        static GameObject CreatePanel(
            Transform parent,
            string name,
            Color color)
        {
            GameObject target = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            target.transform.SetParent(parent, false);
            target.GetComponent<Image>().color = color;
            return target;
        }

        static Text CreateText(
            Transform parent,
            string value,
            int fontSize,
            FontStyle style,
            Color color)
        {
            GameObject target = new GameObject(
                "Text",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            target.transform.SetParent(parent, false);
            Text text = target.GetComponent<Text>();
            text.font = SharedFont;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        static Button CreateButton(
            Transform parent,
            string label,
            Color color)
        {
            GameObject target = CreatePanel(
                parent,
                "Button_" + label,
                color);
            Button button = target.AddComponent<Button>();
            Text text = CreateText(
                target.transform,
                label,
                16,
                FontStyle.Bold,
                Color.white);
            Stretch(text.rectTransform);
            return button;
        }

        static RawImage CreateRawImage(Transform parent)
        {
            GameObject target = new GameObject(
                "Preview",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage));
            target.transform.SetParent(parent, false);
            RawImage image = target.GetComponent<RawImage>();
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        static Font SharedFont =>
            sharedFont != null
                ? sharedFont
                : sharedFont =
                    Resources.GetBuiltinResource<Font>(
                        "LegacyRuntime.ttf");

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static void StretchWithMargin(
            RectTransform rect,
            float margin)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(margin, 4f);
            rect.offsetMax = new Vector2(-margin, -4f);
        }

        static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }
    }
}
