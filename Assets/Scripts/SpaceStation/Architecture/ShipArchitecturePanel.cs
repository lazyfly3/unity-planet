using System;
using UnityEngine;
using UnityEngine.UI;
using UnityPlanet.SpacecraftArchitecture;
using UnityPlanet.SpaceStation.Enhancement;

namespace UnityPlanet.SpaceStation.Architecture
{
    /// <summary>
    /// Resolution-independent tier marker. The protocol backdrop already owns
    /// the long circuit track, so this graphic supplies a compact hexagonal
    /// energy cell instead of placing a flat square over the artwork.
    /// </summary>
    public sealed class ShipArchitectureTierNodeGraphic : MaskableGraphic
    {
        Color accent = new Color(0.12f, 0.88f, 0.94f, 1f);
        bool unlocked;
        bool current;
        bool terminal;

        public void SetVisualState(
            Color stateAccent,
            bool isUnlocked,
            bool isCurrent,
            bool isTerminal)
        {
            accent = stateAccent;
            unlocked = isUnlocked;
            current = isCurrent;
            terminal = isTerminal;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            Rect rect = GetPixelAdjustedRect();
            Vector2 center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.42f;
            Color ring = unlocked
                ? accent
                : new Color(0.18f, 0.34f, 0.40f, 0.72f);
            Color fill = unlocked
                ? new Color(accent.r * 0.16f, accent.g * 0.22f,
                    accent.b * 0.24f, 0.92f)
                : new Color(0.015f, 0.055f, 0.075f, 0.95f);

            if (current)
            {
                Color glow = terminal
                    ? new Color(1f, 0.72f, 0.22f, 0.25f)
                    : new Color(accent.r, accent.g, accent.b, 0.22f);
                AddHexRing(helper, center, radius * 1.12f,
                    radius * 1.01f, glow);
            }

            AddFilledHex(helper, center, radius * 0.86f, fill);
            AddHexRing(helper, center, radius, radius * 0.78f, ring);
            AddHexRing(
                helper,
                center,
                radius * 0.66f,
                radius * 0.58f,
                new Color(ring.r, ring.g, ring.b,
                    unlocked ? 0.82f : 0.38f));

            if (unlocked)
            {
                AddDiamond(
                    helper,
                    center,
                    radius * (current ? 0.34f : 0.24f),
                    terminal
                        ? new Color(1f, 0.78f, 0.34f, 0.95f)
                        : new Color(accent.r, accent.g, accent.b, 0.95f));
            }
        }

        static void AddFilledHex(
            VertexHelper helper,
            Vector2 center,
            float radius,
            Color color)
        {
            int start = helper.currentVertCount;
            helper.AddVert(center, color, Vector2.zero);
            for (int index = 0; index < 6; index++)
            {
                float angle = Mathf.Deg2Rad * (30f + index * 60f);
                helper.AddVert(
                    center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) *
                    radius,
                    color,
                    Vector2.zero);
            }
            for (int index = 0; index < 6; index++)
                helper.AddTriangle(
                    start,
                    start + 1 + index,
                    start + 1 + (index + 1) % 6);
        }

        static void AddHexRing(
            VertexHelper helper,
            Vector2 center,
            float outerRadius,
            float innerRadius,
            Color color)
        {
            int start = helper.currentVertCount;
            for (int index = 0; index < 6; index++)
            {
                float angle = Mathf.Deg2Rad * (30f + index * 60f);
                Vector2 direction =
                    new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                helper.AddVert(
                    center + direction * outerRadius,
                    color,
                    Vector2.zero);
                helper.AddVert(
                    center + direction * innerRadius,
                    color,
                    Vector2.zero);
            }
            for (int index = 0; index < 6; index++)
            {
                int next = (index + 1) % 6;
                int outer = start + index * 2;
                int inner = outer + 1;
                int nextOuter = start + next * 2;
                int nextInner = nextOuter + 1;
                helper.AddTriangle(outer, nextOuter, inner);
                helper.AddTriangle(nextOuter, nextInner, inner);
            }
        }

        static void AddDiamond(
            VertexHelper helper,
            Vector2 center,
            float radius,
            Color color)
        {
            int start = helper.currentVertCount;
            helper.AddVert(center + Vector2.up * radius, color, Vector2.zero);
            helper.AddVert(center + Vector2.right * radius, color, Vector2.zero);
            helper.AddVert(center + Vector2.down * radius, color, Vector2.zero);
            helper.AddVert(center + Vector2.left * radius, color, Vector2.zero);
            helper.AddTriangle(start, start + 1, start + 2);
            helper.AddTriangle(start, start + 2, start + 3);
        }
    }

    [DisallowMultipleComponent]
    public sealed class ShipArchitecturePanel : MonoBehaviour
    {
        sealed class BranchView
        {
            public ShipArchitectureBranch Branch;
            public Text Current;
            public Text Detail;
            public Button Upgrade;
            public Text UpgradeLabel;
            public ShipArchitectureTierNodeGraphic[] Nodes;
            public Text[] NodeLabels;
        }

        static readonly Color Cyan =
            new Color(0.12f, 0.88f, 0.94f, 1f);
        static readonly Color Gold =
            new Color(1f, 0.72f, 0.22f, 1f);
        static readonly Color Locked =
            new Color(0.055f, 0.13f, 0.18f, 0.96f);

        GameObject root;
        Font font;
        Sprite coinSprite;
        Action backAction;
        Text walletText;
        Text statusText;
        BranchView moduleBranch;
        BranchView cpuBranch;
        bool initialized;

        public bool IsOpen => root != null && root.activeSelf;

        public void Initialize(
            Transform canvasParent,
            Font sharedFont,
            Sprite sharedCoinSprite,
            Action onBack)
        {
            if (initialized || canvasParent == null)
                return;

            initialized = true;
            font = sharedFont;
            coinSprite = sharedCoinSprite;
            backAction = onBack;
            Build(canvasParent);
            root.SetActive(false);
            GalaxyCurrencyService.BalanceChanged += HandleBalanceChanged;
            ShipArchitectureProgressService.ProgressChanged +=
                HandleProgressChanged;
        }

        public void Open()
        {
            if (root == null)
                return;
            root.SetActive(true);
            statusText.text = string.Empty;
            Refresh();
        }

        public void Close()
        {
            if (root != null)
                root.SetActive(false);
        }

        void OnDestroy()
        {
            GalaxyCurrencyService.BalanceChanged -= HandleBalanceChanged;
            ShipArchitectureProgressService.ProgressChanged -=
                HandleProgressChanged;
        }

        void HandleBalanceChanged(int balance)
        {
            if (walletText != null)
                walletText.text = balance.ToString("N0") + "  银河币";
            RefreshBranch(moduleBranch, balance);
            RefreshBranch(cpuBranch, balance);
        }

        void HandleProgressChanged()
        {
            if (IsOpen)
                Refresh();
        }

        void Refresh()
        {
            GalaxyEnhancementProgressData wallet =
                GalaxyCurrencyService.LoadOrCreate();
            int balance = wallet.galaxyCoins;
            if (walletText != null)
                walletText.text = balance.ToString("N0") + "  银河币";
            RefreshBranch(moduleBranch, balance);
            RefreshBranch(cpuBranch, balance);
        }

        void RefreshBranch(BranchView view, int balance)
        {
            if (view == null)
                return;

            int level = ShipArchitectureProgressService.GetLevel(view.Branch);
            int capacity = ShipArchitectureProgressService.GetCapacity(
                view.Branch,
                level);
            string unit = view.Branch ==
                ShipArchitectureBranch.ModuleCapacity
                    ? "模块"
                    : "CPU";
            view.Current.text =
                "当前授权  " + capacity.ToString("N0") + " " + unit;
            view.Detail.text = view.Branch ==
                ShipArchitectureBranch.ModuleCapacity
                    ? "决定飞船最多可保存、装载和试飞的模块总数"
                    : "不同模块消耗不同 CPU；所有模块消耗之和不得超过授权值";

            for (int index = 0; index < view.Nodes.Length; index++)
            {
                bool current = index == level;
                bool unlocked = index <= level;
                bool terminal = index == view.Nodes.Length - 1;
                Color nodeColor = current && terminal
                    ? Gold
                    : current
                        ? Cyan
                        : unlocked
                            ? new Color(0.06f, 0.55f, 0.62f, 1f)
                            : Locked;
                view.Nodes[index].SetVisualState(
                    nodeColor,
                    unlocked,
                    current,
                    terminal);
                view.NodeLabels[index].text =
                    ShipArchitectureProgressService.GetCapacity(
                            view.Branch,
                            index)
                        .ToString("N0");
                view.NodeLabels[index].color = current && terminal
                    ? Gold
                    : current
                        ? Color.white
                        : unlocked
                            ? new Color(0.82f, 0.96f, 1f)
                            : new Color(0.56f, 0.72f, 0.79f);
            }

            bool hasNext =
                ShipArchitectureProgressService.TryGetNextUpgrade(
                    view.Branch,
                    out int nextCapacity,
                    out int cost);
            view.Upgrade.interactable = hasNext && balance >= cost;
            if (!hasNext)
            {
                view.UpgradeLabel.text = "已达最高授权";
            }
            else
            {
                view.UpgradeLabel.text =
                    "升级至 " + nextCapacity.ToString("N0") +
                    "   ·   " + cost.ToString("N0") + " 银河币";
            }
        }

        void PurchaseUpgrade(ShipArchitectureBranch branch)
        {
            if (!ShipArchitectureProgressService.TryGetNextUpgrade(
                    branch,
                    out _,
                    out int cost))
            {
                statusText.text = "该架构分支已达到最高授权。";
                Refresh();
                return;
            }

            if (!GalaxyCurrencyService.TrySpendGalaxyCoins(
                    cost,
                    out _,
                    out string walletMessage))
            {
                statusText.text = walletMessage;
                Refresh();
                return;
            }

            if (!ShipArchitectureProgressService.TryUpgrade(
                    branch,
                    out string upgradeMessage))
            {
                // The wallet and architecture progress are separate files.
                // Refund immediately if the architecture save did not commit.
                GalaxyCurrencyService.AddGalaxyCoins(cost);
                statusText.text = upgradeMessage + " 已退回本次费用。";
                Refresh();
                return;
            }

            statusText.text = upgradeMessage;
            Refresh();
        }

        void Build(Transform parent)
        {
            GameObject rootObject = new GameObject(
                "ShipArchitectureProtocolScreen",
                typeof(RectTransform),
                typeof(Image));
            rootObject.transform.SetParent(parent, false);
            root = rootObject;
            Image screenBlocker = rootObject.GetComponent<Image>();
            screenBlocker.color = Color.black;
            SetStretch(screenBlocker.rectTransform, 0f, 0f, 0f, 0f);

            GameObject frameObject = new GameObject(
                "ArchitectureFrame",
                typeof(RectTransform),
                typeof(RawImage),
                typeof(AspectRatioFitter));
            frameObject.transform.SetParent(rootObject.transform, false);
            RawImage background = frameObject.GetComponent<RawImage>();
            background.texture = Resources.Load<Texture2D>(
                "UI/Architecture/ShipArchitectureProtocolBackdrop");
            background.color = background.texture != null
                ? Color.white
                : new Color(0f, 0.02f, 0.04f, 0.99f);
            SetStretch(background.rectTransform, 0f, 0f, 0f, 0f);
            AspectRatioFitter fitter =
                frameObject.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 1672f / 941f;
            Transform surface = frameObject.transform;

            Text title = CreateText(
                surface,
                "ArchitectureTitle",
                "舰体架构协议",
                40,
                TextAnchor.MiddleCenter,
                new Color(0.76f, 0.98f, 1f));
            SetAnchoredRect(
                title.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -28f),
                new Vector2(720f, 58f));

            Text subtitle = CreateText(
                surface,
                "ArchitectureSubtitle",
                "确定性装配授权  ·  与随机强化和技能装配相互独立",
                18,
                TextAnchor.MiddleCenter,
                new Color(0.42f, 0.78f, 0.84f));
            SetAnchoredRect(
                subtitle.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -92f),
                new Vector2(780f, 34f));

            BuildWallet(surface);
            moduleBranch = BuildBranch(
                surface,
                "ModuleCapacityBranch",
                "装配框架",
                ShipArchitectureBranch.ModuleCapacity,
                new Vector2(0.075f, 0.27f),
                new Vector2(0.475f, 0.72f),
                0.16f,
                0.90f);
            cpuBranch = BuildBranch(
                surface,
                "CpuCapacityBranch",
                "运算核心",
                ShipArchitectureBranch.CpuCapacity,
                new Vector2(0.525f, 0.27f),
                new Vector2(0.925f, 0.72f),
                0.07f,
                0.80f);

            Button back = CreateButton(
                surface,
                "BackToCelesteDialogue",
                "返回对话  Esc",
                new Color(0.08f, 0.25f, 0.32f, 0.96f),
                out _);
            SetAnchoredRect(
                back.GetComponent<RectTransform>(),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(38f, 28f),
                new Vector2(230f, 54f));
            back.onClick.AddListener(() => backAction?.Invoke());

            statusText = CreateText(
                surface,
                "ArchitectureStatus",
                string.Empty,
                18,
                TextAnchor.MiddleCenter,
                new Color(0.66f, 0.91f, 0.94f));
            statusText.resizeTextForBestFit = true;
            statusText.resizeTextMinSize = 14;
            statusText.resizeTextMaxSize = 18;
            SetAnchoredRect(
                statusText.rectTransform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 50f),
                new Vector2(580f, 50f));

        }

        void BuildWallet(Transform parent)
        {
            Image panel = CreateImage(
                parent,
                "ArchitectureWallet",
                new Color(0.015f, 0.075f, 0.105f, 0.94f));
            SetAnchoredRect(
                panel.rectTransform,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-30f, -28f),
                new Vector2(250f, 58f));
            Image icon = CreateImage(
                panel.transform,
                "Coin",
                Color.white,
                coinSprite);
            SetAnchoredRect(
                icon.rectTransform,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(10f, 0f),
                new Vector2(40f, 40f));
            icon.preserveAspect = true;
            walletText = CreateText(
                panel.transform,
                "Amount",
                string.Empty,
                20,
                TextAnchor.MiddleRight,
                Color.white);
            SetStretch(walletText.rectTransform, 56f, 12f, 4f, 4f);
        }

        BranchView BuildBranch(
            Transform parent,
            string name,
            string label,
            ShipArchitectureBranch branch,
            Vector2 anchorMin,
            Vector2 anchorMax,
            float nodeAnchorStart,
            float nodeAnchorEnd)
        {
            GameObject group = new GameObject(name, typeof(RectTransform));
            group.transform.SetParent(parent, false);
            RectTransform groupRect = group.GetComponent<RectTransform>();
            groupRect.anchorMin = anchorMin;
            groupRect.anchorMax = anchorMax;
            groupRect.offsetMin = groupRect.offsetMax = Vector2.zero;

            Text heading = CreateText(
                group.transform,
                "Heading",
                label,
                28,
                TextAnchor.MiddleCenter,
                branch == ShipArchitectureBranch.ModuleCapacity
                    ? new Color(0.35f, 0.96f, 1f)
                    : new Color(0.62f, 0.88f, 1f));
            SetAnchoredRect(
                heading.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, 4f),
                new Vector2(320f, 42f));

            Text current = CreateText(
                group.transform,
                "CurrentCapacity",
                string.Empty,
                25,
                TextAnchor.MiddleCenter,
                Color.white);
            SetAnchoredRect(
                current.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -36f),
                new Vector2(460f, 42f));

            Text detail = CreateText(
                group.transform,
                "CapacityDescription",
                string.Empty,
                16,
                TextAnchor.MiddleCenter,
                new Color(0.60f, 0.80f, 0.84f));
            detail.resizeTextForBestFit = true;
            detail.resizeTextMinSize = 13;
            detail.resizeTextMaxSize = 16;
            SetAnchoredRect(
                detail.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -66f),
                new Vector2(520f, 42f));

            int tierCount = ShipArchitectureProgressService.TierCount;
            var nodes = new ShipArchitectureTierNodeGraphic[tierCount];
            var nodeLabels = new Text[tierCount];
            for (int index = 0; index < tierCount; index++)
            {
                float normalized = tierCount <= 1
                    ? 0.5f
                    : index / (float)(tierCount - 1);
                float x = Mathf.Lerp(
                    nodeAnchorStart,
                    nodeAnchorEnd,
                    normalized);
                GameObject nodeObject = new GameObject(
                    "TierNode_" + index,
                    typeof(RectTransform),
                    typeof(ShipArchitectureTierNodeGraphic));
                nodeObject.transform.SetParent(group.transform, false);
                ShipArchitectureTierNodeGraphic node =
                    nodeObject.GetComponent<
                        ShipArchitectureTierNodeGraphic>();
                node.raycastTarget = false;
                SetAnchoredRect(
                    node.rectTransform,
                    new Vector2(x, 0.44f),
                    new Vector2(x, 0.44f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(46f, 46f));
                Text nodeLabel = CreateText(
                    group.transform,
                    "TierValue_" + index,
                    string.Empty,
                    15,
                    TextAnchor.MiddleCenter,
                    Color.white);
                nodeLabel.fontStyle = FontStyle.Bold;
                nodeLabel.resizeTextForBestFit = true;
                nodeLabel.resizeTextMinSize = 11;
                nodeLabel.resizeTextMaxSize = 15;
                Outline labelOutline =
                    nodeLabel.gameObject.AddComponent<Outline>();
                labelOutline.effectColor =
                    new Color(0f, 0.025f, 0.045f, 0.96f);
                labelOutline.effectDistance = new Vector2(1.2f, -1.2f);
                SetAnchoredRect(
                    nodeLabel.rectTransform,
                    new Vector2(x, 0.44f),
                    new Vector2(x, 0.44f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0f, -35f),
                    new Vector2(82f, 24f));
                nodes[index] = node;
                nodeLabels[index] = nodeLabel;
            }

            Button upgrade = CreateButton(
                group.transform,
                "UpgradeAuthorization",
                string.Empty,
                new Color(0.025f, 0.48f, 0.55f, 0.98f),
                out Text upgradeLabel);
            SetAnchoredRect(
                upgrade.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 18f),
                new Vector2(330f, 56f));
            upgradeLabel.resizeTextForBestFit = true;
            upgradeLabel.resizeTextMinSize = 14;
            upgradeLabel.resizeTextMaxSize = 19;
            upgrade.onClick.AddListener(() => PurchaseUpgrade(branch));

            return new BranchView
            {
                Branch = branch,
                Current = current,
                Detail = detail,
                Upgrade = upgrade,
                UpgradeLabel = upgradeLabel,
                Nodes = nodes,
                NodeLabels = nodeLabels
            };
        }

        Image CreateImage(
            Transform parent,
            string name,
            Color color,
            Sprite sprite = null)
        {
            GameObject value = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image));
            value.transform.SetParent(parent, false);
            Image image = value.GetComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            return image;
        }

        Text CreateText(
            Transform parent,
            string name,
            string value,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            GameObject gameObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Text));
            gameObject.transform.SetParent(parent, false);
            Text text = gameObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.text = value;
            text.supportRichText = true;
            text.raycastTarget = false;
            return text;
        }

        Button CreateButton(
            Transform parent,
            string name,
            string label,
            Color color,
            out Text labelText)
        {
            GameObject gameObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            gameObject.transform.SetParent(parent, false);
            Image image = gameObject.GetComponent<Image>();
            image.color = color;
            Button button = gameObject.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor =
                new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor =
                new Color(0.76f, 0.86f, 0.9f, 1f);
            colors.disabledColor =
                new Color(0.28f, 0.33f, 0.36f, 0.76f);
            button.colors = colors;
            labelText = CreateText(
                gameObject.transform,
                "Label",
                label,
                19,
                TextAnchor.MiddleCenter,
                Color.white);
            SetStretch(labelText.rectTransform, 8f, 8f, 4f, 4f);
            return button;
        }

        static void SetStretch(
            RectTransform rect,
            float left,
            float right,
            float bottom,
            float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        static void SetAnchoredRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
