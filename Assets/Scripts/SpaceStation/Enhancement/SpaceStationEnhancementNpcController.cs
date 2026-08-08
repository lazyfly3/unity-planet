using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ModularAssembly;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityPlanet.SpaceStation.Skills;

namespace UnityPlanet.SpaceStation.Enhancement
{
    [DisallowMultipleComponent]
    public sealed class SpaceStationEnhancementNpcController : MonoBehaviour
    {
        const string StationSceneName = "SpaceStationUpgradeTest";
        const string NpcObjectName = "NPC_Celeste_Floor01_10";
        const float InteractionDistance = 3.2f;

        sealed class CardView
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Button Button;
            public Image Frame;
            public Text Rarity;
            public Text Title;
            public Text Description;
            public Text Owned;
            public Text Price;
        }

        Transform player;
        SpaceStationFirstPersonController firstPersonController;
        GalaxyEnhancementProgressData progress;
        Font font;
        Sprite cardBackSprite;
        Sprite normalCardSprite;
        Sprite goldCardSprite;
        Sprite coinSprite;
        Texture2D celestePortraitSheet;

        GameObject canvasRoot;
        GameObject promptRoot;
        GameObject dialogueRoot;
        GameObject enhancementRoot;
        Text walletText;
        Text enhancementWalletText;
        Text scanText;
        Text statusText;
        RawImage dialoguePortrait;
        readonly CardView[] cards = new CardView[
            EnhancementPcgRules.CardsPerDraw];
        bool interfaceOpen;
        bool enhancementOpen;
        bool transitionBusy;
        Coroutine dealRoutine;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RegisterSceneHook()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallForInitialScene()
        {
            InstallForScene(SceneManager.GetActiveScene());
        }

        static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            InstallForScene(scene);
        }

        static void InstallForScene(Scene scene)
        {
            if (!scene.IsValid() || scene.name != StationSceneName)
            {
                return;
            }

            GameObject npc = FindNpc(scene);
            if (npc != null &&
                npc.GetComponent<
                    SpaceStationEnhancementNpcController>() == null)
            {
                npc.AddComponent<
                    SpaceStationEnhancementNpcController>();
            }
        }

        static GameObject FindNpc(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform[] descendants =
                    root.GetComponentsInChildren<Transform>(true);
                foreach (Transform candidate in descendants)
                {
                    if (candidate.name == NpcObjectName)
                    {
                        return candidate.gameObject;
                    }
                }
            }
            return null;
        }

        void Start()
        {
            SpaceStationFirstPersonController walker =
                FindObjectOfType<SpaceStationFirstPersonController>();
            firstPersonController = walker;
            player = walker != null ? walker.transform : null;
            font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            cardBackSprite = Resources.Load<Sprite>(
                "UI/Enhancement/EnhancementCardBack");
            normalCardSprite = Resources.Load<Sprite>(
                "UI/Enhancement/EnhancementCardNormal");
            goldCardSprite = Resources.Load<Sprite>(
                "UI/Enhancement/EnhancementCardGold");
            coinSprite = Resources.Load<Sprite>(
                "UI/Enhancement/GalacticCoin");
            celestePortraitSheet = Resources.Load<Texture2D>(
                "UI/Characters/CelesteExpressions");

            progress = GalaxyCurrencyService.LoadOrCreate();
            EnsureOffers();
            BuildInterface();
            RefreshWallet(progress.galaxyCoins);
            RefreshCards();
            GalaxyCurrencyService.BalanceChanged += HandleBalanceChanged;
        }

        void Update()
        {
            if (canvasRoot == null)
            {
                return;
            }

            bool inRange = IsPlayerInRange();
            if (!interfaceOpen)
            {
                promptRoot.SetActive(inRange);
                if (inRange && Input.GetKeyDown(KeyCode.F))
                {
                    OpenDialogue();
                }
                return;
            }

            promptRoot.SetActive(false);
            if (!transitionBusy && Input.GetKeyDown(KeyCode.Escape))
            {
                if (enhancementOpen)
                {
                    OpenDialogue();
                }
                else
                {
                    CloseInterface();
                }
            }
        }

        void OnDestroy()
        {
            GalaxyCurrencyService.BalanceChanged -= HandleBalanceChanged;
            if (canvasRoot != null)
            {
                Destroy(canvasRoot);
            }
            if (interfaceOpen && firstPersonController != null)
            {
                firstPersonController.enabled = true;
            }
        }

        bool IsPlayerInRange()
        {
            if (player == null)
            {
                return false;
            }
            Vector3 offset = player.position - transform.position;
            offset.y *= 0.35f;
            return offset.sqrMagnitude <=
                   InteractionDistance * InteractionDistance;
        }

        void OpenDialogue()
        {
            interfaceOpen = true;
            enhancementOpen = false;
            transitionBusy = false;
            SetDialoguePortrait(new Rect(0f, 0.5f, 0.5f, 0.5f));
            dialogueRoot.SetActive(true);
            enhancementRoot.SetActive(false);
            SetPlayerInputEnabled(false);
        }

        void OpenEnhancement()
        {
            interfaceOpen = true;
            enhancementOpen = true;
            dialogueRoot.SetActive(false);
            enhancementRoot.SetActive(true);
            SetPlayerInputEnabled(false);
            transitionBusy = true;
            RefreshCards();
            RefreshDrawStatus();
            if (dealRoutine != null)
            {
                StopCoroutine(dealRoutine);
            }
            dealRoutine = StartCoroutine(DealCards());
        }

        void CloseInterface()
        {
            interfaceOpen = false;
            enhancementOpen = false;
            transitionBusy = false;
            dialogueRoot.SetActive(false);
            enhancementRoot.SetActive(false);
            SetPlayerInputEnabled(true);
        }

        void SetPlayerInputEnabled(bool enabled)
        {
            if (firstPersonController != null &&
                firstPersonController.enabled != enabled)
            {
                firstPersonController.enabled = enabled;
            }
        }

        void EnsureOffers()
        {
            if (progress.currentOffers == null ||
                progress.currentOffers.Length !=
                EnhancementPcgRules.CardsPerDraw ||
                progress.currentOffers.Any(offer =>
                    offer == null ||
                    (offer.rarity == EnhancementRarity.Gold
                        ? !PlayerSkillCatalog.TryGet(
                            offer.skillId,
                            out _)
                        : !EnhancementTypeCatalog.TryGet(
                            offer.definitionId,
                            out _))))
            {
                GenerateOffers();
            }
        }

        void GenerateOffers()
        {
            ModularBlueprintData blueprint = null;
            var blueprintStore = new ModularBlueprintStore();
            blueprintStore.TryLoad(out blueprint, out _);

            int worldSeed = 7319;
            string slotId = GalaxyLaunchContext.SelectedSlotId;
            if (!string.IsNullOrWhiteSpace(slotId))
            {
                GalaxySaveSlotMetadata metadata =
                    GalaxySaveSlotService.LoadMetadata(slotId);
                if (metadata != null)
                {
                    worldSeed = metadata.worldSeed;
                }
            }

            var ownedStacks = new Dictionary<string, int>(
                StringComparer.Ordinal);
            foreach (AcquiredEnhancementData acquired in
                     progress.acquiredEnhancements)
            {
                if (acquired != null &&
                    !string.IsNullOrWhiteSpace(acquired.definitionId))
                {
                    ownedStacks[acquired.definitionId] =
                        Mathf.Max(0, acquired.stacks);
                }
            }

            var context = new EnhancementGenerationContext
            {
                WorldSeed = worldSeed,
                OfferSequence = progress.offerSequence,
                DrawsWithoutGold = progress.drawsWithoutGold,
                ShipProfile =
                    ShipEnhancementProfile.FromBlueprint(blueprint),
                OwnedStacks = ownedStacks
            };
            progress.currentOffers = EnhancementPcgRules.Generate(
                context,
                out bool containsGold);
            progress.offerSequence++;
            progress.drawsWithoutGold = containsGold
                ? 0
                : progress.drawsWithoutGold + 1;
            progress.currentDrawPaid = false;
            progress.revealedCards = new bool[
                EnhancementPcgRules.CardsPerDraw];
            GalaxyCurrencyService.Save(progress);
        }

        void SelectOffer(int index)
        {
            if (transitionBusy || index < 0 ||
                index >= progress.currentOffers.Length)
            {
                return;
            }

            EnhancementOfferData offer =
                progress.currentOffers[index];
            if (!progress.currentDrawPaid &&
                !GalaxyCurrencyService.TryBeginDraw(
                    progress,
                    EnhancementPcgRules.DrawCost,
                    out string drawMessage))
            {
                statusText.text = drawMessage;
                RefreshCards();
                return;
            }

            if (!progress.revealedCards[index])
            {
                statusText.text =
                    "正在解密第 " + (index + 1) + " 张强化卡……";
                transitionBusy = true;
                RefreshCards();
                StartCoroutine(FlipCard(index));
                return;
            }

            if (!AllCardsRevealed())
            {
                statusText.text = "请先翻开其余卡牌，再免费选择强化。";
                return;
            }

            string message;
            bool acquired;
            if (offer.rarity == EnhancementRarity.Gold)
            {
                acquired = PlayerSkillProgressService.TryAcquireGoldSkill(
                    offer.skillId,
                    out message);
                if (acquired)
                {
                    progress.currentOffers =
                        Array.Empty<EnhancementOfferData>();
                    progress.currentDrawPaid = false;
                    progress.revealedCards = new bool[
                        EnhancementPcgRules.CardsPerDraw];
                    GalaxyCurrencyService.Save(progress);
                }
            }
            else
            {
                acquired = GalaxyCurrencyService.TryAcquireFreeEnhancement(
                    progress,
                    offer,
                    out message);
            }
            if (!acquired)
            {
                statusText.text = message;
                RefreshCards();
                return;
            }

            transitionBusy = true;
            statusText.text = message;
            StartCoroutine(CompleteSelection(index, offer.rarity));
        }

        IEnumerator FlipCard(int index)
        {
            CardView card = cards[index];
            float elapsed = 0f;
            const float halfDuration = 0.18f;
            while (elapsed < halfDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / halfDuration);
                card.Root.localScale = new Vector3(
                    Mathf.Lerp(1f, 0.02f, t),
                    1f,
                    1f);
                yield return null;
            }

            progress.revealedCards[index] = true;
            GalaxyCurrencyService.Save(progress);
            RefreshCards();

            elapsed = 0f;
            while (elapsed < halfDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / halfDuration);
                card.Root.localScale = new Vector3(
                    Mathf.Lerp(0.02f, 1f, t),
                    1f,
                    1f);
                yield return null;
            }
            card.Root.localScale = Vector3.one;

            EnhancementOfferData offer = progress.currentOffers[index];
            if (offer.rarity == EnhancementRarity.Gold)
            {
                StartCoroutine(PlayGoldReveal(card));
            }
            transitionBusy = false;
            RefreshCards();
            statusText.text = AllCardsRevealed()
                ? "三张卡牌已全部揭示，点击任意一张免费安装强化。"
                : "继续点击其余卡背完成翻牌。";
        }

        IEnumerator CompleteSelection(
            int selectedIndex,
            EnhancementRarity rarity)
        {
            CardView selected = cards[selectedIndex];
            float duration = rarity == EnhancementRarity.Gold
                ? 0.7f
                : 0.42f;
            if (rarity == EnhancementRarity.Gold)
            {
                StartCoroutine(PlayGoldReveal(selected));
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float pulse = Mathf.Sin(t * Mathf.PI) *
                              (rarity == EnhancementRarity.Gold
                                  ? 0.09f
                                  : 0.045f);
                selected.Root.localScale =
                    Vector3.one * (1f + pulse);
                yield return null;
            }
            selected.Root.localScale = Vector3.one;

            progress.currentOffers =
                Array.Empty<EnhancementOfferData>();
            GenerateOffers();
            RefreshCards();
            RefreshDrawStatus();
            dealRoutine = StartCoroutine(DealCards());
        }

        IEnumerator DealCards()
        {
            for (int index = 0; index < cards.Length; index++)
            {
                CardView card = cards[index];
                card.Group.alpha = 0f;
                card.Root.localScale = new Vector3(0.04f, 1f, 1f);
                float elapsed = 0f;
                const float duration = 0.24f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    float eased = 1f - Mathf.Pow(1f - t, 3f);
                    card.Group.alpha = eased;
                    card.Root.localScale = new Vector3(
                        Mathf.Lerp(0.04f, 1f, eased),
                        1f,
                        1f);
                    yield return null;
                }
                card.Group.alpha = 1f;
                card.Root.localScale = Vector3.one;

                yield return new WaitForSecondsRealtime(0.08f);
            }
            dealRoutine = null;
            transitionBusy = false;
            RefreshCards();
            RefreshDrawStatus();
        }

        IEnumerator PlayGoldReveal(CardView card)
        {
            const int particleCount = 14;
            var particles = new List<RectTransform>(particleCount);
            var particleImages = new List<Image>(particleCount);
            for (int index = 0; index < particleCount; index++)
            {
                Image particle = CreateImage(
                    card.Root,
                    "GoldSpark_" + index,
                    new Color(1f, 0.72f, 0.16f, 1f));
                RectTransform rect = particle.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(5f, 18f);
                rect.anchoredPosition = Vector2.zero;
                rect.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    index * (360f / particleCount));
                particles.Add(rect);
                particleImages.Add(particle);
            }

            Image scan = CreateImage(
                card.Root,
                "GoldScanLine",
                new Color(1f, 0.82f, 0.28f, 0.9f));
            RectTransform scanRect = scan.rectTransform;
            scanRect.anchorMin = scanRect.anchorMax =
                new Vector2(0.5f, 0.5f);
            scanRect.sizeDelta = new Vector2(230f, 4f);

            float elapsed = 0f;
            const float duration = 0.62f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                for (int index = 0; index < particles.Count; index++)
                {
                    float angle = index * Mathf.PI * 2f /
                                  particleCount;
                    Vector2 direction = new Vector2(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle));
                    particles[index].anchoredPosition = direction *
                        Mathf.Lerp(8f, 145f, t);
                    Color color = particleImages[index].color;
                    color.a = 1f - t;
                    particleImages[index].color = color;
                }
                scanRect.anchoredPosition = new Vector2(
                    0f,
                    Mathf.Lerp(-180f, 180f, t));
                Color scanColor = scan.color;
                scanColor.a = Mathf.Sin(t * Mathf.PI) * 0.9f;
                scan.color = scanColor;
                float pulse = Mathf.Sin(t * Mathf.PI) * 0.035f;
                card.Frame.rectTransform.localScale =
                    Vector3.one * (1f + pulse);
                yield return null;
            }

            card.Frame.rectTransform.localScale = Vector3.one;
            foreach (RectTransform particle in particles)
            {
                Destroy(particle.gameObject);
            }
            Destroy(scan.gameObject);
        }

        void RefreshCards()
        {
            if (cards[0] == null || progress.currentOffers == null ||
                progress.currentOffers.Length != cards.Length)
            {
                return;
            }

            int moduleCount = ReadModuleCount();
            scanText.text = "PCG舰体扫描  ·  模块 " + moduleCount +
                            "  ·  稀有概率 " +
                            Mathf.RoundToInt(
                                EnhancementPcgRules.BaseGoldChance * 100f) +
                            "%  ·  保底进度 " +
                            Mathf.Min(
                                progress.drawsWithoutGold,
                                EnhancementPcgRules.GoldPityDraws) + "/" +
                            EnhancementPcgRules.GoldPityDraws;

            PlayerSkillProgressData skillProgress =
                PlayerSkillProgressService.LoadOrCreate();

            for (int index = 0; index < cards.Length; index++)
            {
                EnhancementOfferData offer =
                    progress.currentOffers[index];
                CardView card = cards[index];
                bool revealed = progress.revealedCards != null &&
                                index < progress.revealedCards.Length &&
                                progress.revealedCards[index];
                bool gold = offer.rarity == EnhancementRarity.Gold;
                PlayerSkillDefinition skillDefinition = null;
                bool skillOffer = gold && PlayerSkillCatalog.TryGet(
                    offer.skillId,
                    out skillDefinition);
                EnhancementTypeCatalog.TryGet(
                    offer.definitionId,
                    out EnhancementTypeDefinition definition);
                card.Frame.sprite = !revealed
                    ? cardBackSprite != null
                        ? cardBackSprite
                        : normalCardSprite
                    : gold
                        ? goldCardSprite
                        : normalCardSprite;
                card.Rarity.text = string.Empty;
                card.Rarity.color = revealed && gold
                    ? new Color(1f, 0.78f, 0.22f)
                    : new Color(0.25f, 0.92f, 1f);
                card.Title.text = revealed
                    ? skillOffer
                        ? skillDefinition.DisplayName
                        : definition.DisplayName
                    : string.Empty;
                card.Title.color = revealed && gold
                    ? new Color(1f, 0.86f, 0.43f)
                    : Color.white;
                card.Description.text = revealed
                    ? skillOffer
                        ? "主动技能 · 金卡专属\n" +
                          skillDefinition.Description
                        : definition.FormatDescription(offer.magnitude)
                    : string.Empty;
                int stacks = skillOffer
                    ? 0
                    : FindOwnedStacks(definition.Id);
                PlayerSkillProgressEntry skillEntry = skillOffer
                    ? PlayerSkillProgressService.Find(
                        skillProgress,
                        offer.skillId)
                    : null;
                card.Owned.text = revealed
                    ? skillOffer
                        ? skillEntry == null
                            ? "尚未获得"
                            : "技能等级 Lv." + skillEntry.level +
                              "/" + skillDefinition.MaximumLevel
                        : "已安装 " + stacks + "/" +
                          definition.MaximumStacks
                    : string.Empty;
                card.Price.text = string.Empty;
                card.Price.color = !progress.currentDrawPaid &&
                                   progress.galaxyCoins <
                                   EnhancementPcgRules.DrawCost
                    ? new Color(1f, 0.38f, 0.3f)
                    : new Color(1f, 0.82f, 0.3f);
                card.Button.interactable =
                    !transitionBusy &&
                    (progress.currentDrawPaid ||
                     progress.galaxyCoins >=
                     EnhancementPcgRules.DrawCost) &&
                    (!revealed || !AllCardsRevealed() ||
                     skillOffer ||
                     stacks < definition.MaximumStacks);
            }
            RefreshWallet(progress.galaxyCoins);
        }

        void RefreshDrawStatus()
        {
            if (AllCardsRevealed())
            {
                statusText.text =
                    "三张卡牌已全部揭示，点击任意一张免费安装强化。";
                return;
            }

            if (!progress.currentDrawPaid &&
                progress.galaxyCoins < EnhancementPcgRules.DrawCost)
            {
                statusText.text = "银河币不足：每轮需要 " +
                                  EnhancementPcgRules.DrawCost +
                                  " 银河币。";
                return;
            }

            statusText.text = progress.currentDrawPaid
                ? "继续点击卡背完成翻牌。"
                : "点击任意卡背支付 " + EnhancementPcgRules.DrawCost +
                  " 银河币；本轮只扣费一次。";
        }

        void HandleBalanceChanged(int balance)
        {
            if (progress != null)
            {
                progress.galaxyCoins = Mathf.Max(0, balance);
            }
            RefreshWallet(balance);

            if (progress == null || cards[0] == null || transitionBusy ||
                progress.currentOffers == null ||
                progress.currentOffers.Length != cards.Length)
            {
                return;
            }

            RefreshCards();
            if (enhancementOpen)
            {
                RefreshDrawStatus();
            }
        }

        int ReadModuleCount()
        {
            var store = new ModularBlueprintStore();
            return store.TryLoad(
                       out ModularBlueprintData blueprint,
                       out _) &&
                   blueprint.modules != null
                ? blueprint.modules.Length
                : 0;
        }

        int FindOwnedStacks(string definitionId)
        {
            AcquiredEnhancementData acquired =
                progress.acquiredEnhancements.FirstOrDefault(item =>
                    item != null &&
                    item.definitionId == definitionId);
            return acquired != null ? acquired.stacks : 0;
        }

        bool AllCardsRevealed()
        {
            return progress.revealedCards != null &&
                   progress.revealedCards.Length == cards.Length &&
                   progress.revealedCards.All(value => value);
        }

        void RefreshWallet(int balance)
        {
            if (walletText != null)
            {
                walletText.text = balance.ToString("N0") + "  银河币";
            }
            if (enhancementWalletText != null)
            {
                enhancementWalletText.text =
                    balance.ToString("N0") + "  银河币";
            }
        }

        void BuildInterface()
        {
            EnsureEventSystem();
            canvasRoot = new GameObject(
                "SpaceStationEnhancementUI",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 7200;
            CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;

            BuildWallet(canvasRoot.transform);
            BuildPrompt(canvasRoot.transform);
            BuildDialogue(canvasRoot.transform);
            BuildEnhancementScreen(canvasRoot.transform);
            promptRoot.SetActive(false);
            dialogueRoot.SetActive(false);
            enhancementRoot.SetActive(false);
        }

        void BuildWallet(Transform parent)
        {
            Image panel = CreateImage(
                parent,
                "GalaxyCoinHud",
                new Color(0.015f, 0.075f, 0.105f, 0.96f));
            RectTransform rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24f, -22f);
            rect.sizeDelta = new Vector2(260f, 70f);
            panel.raycastTarget = false;

            Image accent = CreateImage(
                panel.transform,
                "WalletAccent",
                new Color(0.08f, 0.86f, 0.9f, 1f));
            SetAnchoredRect(
                accent.rectTransform,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                Vector2.zero,
                new Vector2(0f, 3f));

            Image icon = CreateImage(
                panel.transform,
                "CoinIcon",
                Color.white,
                coinSprite);
            RectTransform iconRect = icon.rectTransform;
            iconRect.anchorMin = iconRect.anchorMax =
                new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(14f, 0f);
            iconRect.sizeDelta = new Vector2(48f, 48f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            walletText = CreateText(
                panel.transform,
                "WalletAmount",
                "0  银河币",
                22,
                TextAnchor.MiddleRight,
                Color.white);
            SetStretch(walletText.rectTransform, 68f, 14f, 8f, 8f);
            walletText.raycastTarget = false;
        }

        void BuildPrompt(Transform parent)
        {
            Image panel = CreateImage(
                parent,
                "NpcInteractionPrompt",
                new Color(0.015f, 0.06f, 0.085f, 0.94f));
            promptRoot = panel.gameObject;
            RectTransform rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 36f);
            rect.sizeDelta = new Vector2(520f, 58f);
            Text text = CreateText(
                panel.transform,
                "PromptText",
                "按 F 与塞莱斯特交谈  ·  舰体强化工程师",
                20,
                TextAnchor.MiddleCenter,
                new Color(0.68f, 0.96f, 1f));
            SetStretch(text.rectTransform, 12f, 12f, 6f, 6f);
        }

        void BuildDialogue(Transform parent)
        {
            dialogueRoot = CreateFullscreenDim(
                parent,
                "CelesteDialogue");
            Image panel = CreateImage(
                dialogueRoot.transform,
                "DialoguePanel",
                new Color(0.012f, 0.055f, 0.078f, 0.985f));
            RectTransform rect = panel.rectTransform;
            rect.anchorMin = new Vector2(0.08f, 0.055f);
            rect.anchorMax = new Vector2(0.92f, 0.34f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            Image accent = CreateImage(
                panel.transform,
                "DialogueAccent",
                new Color(0.08f, 0.88f, 0.92f, 1f));
            SetAnchoredRect(
                accent.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                Vector2.zero,
                new Vector2(0f, 4f));

            GameObject portraitObject = new GameObject(
                "CelesteExpressionPortrait",
                typeof(RectTransform),
                typeof(RawImage));
            portraitObject.transform.SetParent(panel.transform, false);
            dialoguePortrait = portraitObject.GetComponent<RawImage>();
            dialoguePortrait.texture = celestePortraitSheet;
            dialoguePortrait.color = celestePortraitSheet != null
                ? Color.white
                : Color.clear;
            dialoguePortrait.raycastTarget = false;
            SetAnchoredRect(
                dialoguePortrait.rectTransform,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(12f, 0f),
                new Vector2(280f, 280f));
            SetDialoguePortrait(new Rect(0f, 0.5f, 0.5f, 0.5f));

            Text name = CreateText(
                panel.transform,
                "SpeakerName",
                "塞莱斯特  ·  舰体强化工程师",
                26,
                TextAnchor.UpperLeft,
                new Color(0.24f, 0.95f, 1f));
            SetAnchoredRect(
                name.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(300f, -20f),
                new Vector2(520f, 42f));

            Text body = CreateText(
                panel.transform,
                "DialogueBody",
                "我会扫描你当前保存的模块飞船，再根据构筑结构推演三种强化方案。稀有方案会使用特殊边框与揭示特效。需要开始推演吗？",
                21,
                TextAnchor.UpperLeft,
                new Color(0.88f, 0.94f, 0.97f));
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            body.resizeTextForBestFit = true;
            body.resizeTextMinSize = 17;
            body.resizeTextMaxSize = 21;
            SetAnchoredRect(
                body.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(300f, -72f),
                new Vector2(520f, 140f));

            Button enhance = CreateButton(
                panel.transform,
                "EnhanceOption",
                "强化",
                new Color(0.04f, 0.62f, 0.64f, 1f));
            SetAnchoredRect(
                enhance.GetComponent<RectTransform>(),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-34f, 38f),
                new Vector2(220f, 58f));
            enhance.onClick.AddListener(OpenEnhancement);
            AddPortraitInteraction(
                enhance,
                new Rect(0.5f, 0.5f, 0.5f, 0.5f),
                new Rect(0.5f, 0f, 0.5f, 0.5f));

            Button leave = CreateButton(
                panel.transform,
                "LeaveOption",
                "离开",
                new Color(0.16f, 0.25f, 0.30f, 1f));
            SetAnchoredRect(
                leave.GetComponent<RectTransform>(),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-34f, -38f),
                new Vector2(220f, 58f));
            leave.onClick.AddListener(CloseInterface);
            AddPortraitInteraction(
                leave,
                new Rect(0f, 0f, 0.5f, 0.5f),
                new Rect(0f, 0f, 0.5f, 0.5f));
        }

        void AddPortraitInteraction(
            Button button,
            Rect hoverUv,
            Rect pressedUv)
        {
            EventTrigger trigger = button.gameObject.AddComponent<EventTrigger>();
            trigger.triggers = new List<EventTrigger.Entry>();

            var enter = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerEnter
            };
            enter.callback.AddListener(_ => SetDialoguePortrait(hoverUv));
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerExit
            };
            exit.callback.AddListener(_ => SetDialoguePortrait(
                new Rect(0f, 0.5f, 0.5f, 0.5f)));
            trigger.triggers.Add(exit);

            var down = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerDown
            };
            down.callback.AddListener(_ => SetDialoguePortrait(pressedUv));
            trigger.triggers.Add(down);
        }

        void SetDialoguePortrait(Rect uvRect)
        {
            if (dialoguePortrait != null)
                dialoguePortrait.uvRect = uvRect;
        }

        void BuildEnhancementScreen(Transform parent)
        {
            enhancementRoot = CreateFullscreenDim(
                parent,
                "EnhancementDrawScreen");
            Image backdrop = enhancementRoot.GetComponent<Image>();
            backdrop.color = new Color(0f, 0.012f, 0.025f, 0.965f);

            Text header = CreateText(
                enhancementRoot.transform,
                "Header",
                "舰体强化推演",
                34,
                TextAnchor.MiddleCenter,
                new Color(0.78f, 0.98f, 1f));
            SetAnchoredRect(
                header.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -24f),
                new Vector2(520f, 54f));

            scanText = CreateText(
                enhancementRoot.transform,
                "PcgScanSummary",
                string.Empty,
                16,
                TextAnchor.MiddleCenter,
                new Color(0.44f, 0.82f, 0.88f));
            SetAnchoredRect(
                scanText.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -74f),
                new Vector2(800f, 30f));

            Image walletPanel = CreateImage(
                enhancementRoot.transform,
                "EnhancementWallet",
                new Color(0.03f, 0.10f, 0.13f, 0.96f));
            SetAnchoredRect(
                walletPanel.rectTransform,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-32f, -24f),
                new Vector2(250f, 56f));
            Image smallCoin = CreateImage(
                walletPanel.transform,
                "Coin",
                Color.white,
                coinSprite);
            SetAnchoredRect(
                smallCoin.rectTransform,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(10f, 0f),
                new Vector2(42f, 42f));
            smallCoin.preserveAspect = true;
            enhancementWalletText = CreateText(
                walletPanel.transform,
                "Amount",
                string.Empty,
                20,
                TextAnchor.MiddleRight,
                Color.white);
            SetStretch(
                enhancementWalletText.rectTransform,
                60f,
                12f,
                5f,
                5f);

            Button close = CreateButton(
                enhancementRoot.transform,
                "LeaveEnhancement",
                "离开强化  Esc",
                new Color(0.12f, 0.28f, 0.34f, 1f));
            SetAnchoredRect(
                close.GetComponent<RectTransform>(),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(28f, -28f),
                new Vector2(190f, 52f));
            close.onClick.AddListener(OpenDialogue);

            for (int index = 0; index < cards.Length; index++)
            {
                cards[index] = CreateCard(
                    enhancementRoot.transform,
                    index,
                    (index - 1) * 350f);
            }

            statusText = CreateText(
                enhancementRoot.transform,
                "Status",
                "点击任意卡背支付本轮费用；三张全部翻开后免费选择一张。",
                18,
                TextAnchor.MiddleCenter,
                new Color(0.68f, 0.88f, 0.92f));
            SetAnchoredRect(
                statusText.rectTransform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 20f),
                new Vector2(920f, 42f));
        }

        CardView CreateCard(
            Transform parent,
            int index,
            float horizontalPosition)
        {
            GameObject rootObject = new GameObject(
                "EnhancementCard_" + index,
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(Image),
                typeof(Button));
            rootObject.transform.SetParent(parent, false);
            RectTransform root =
                rootObject.GetComponent<RectTransform>();
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = new Vector2(horizontalPosition, -4f);
            root.sizeDelta = new Vector2(310f, 580f);
            Image background = rootObject.GetComponent<Image>();
            background.color = new Color(0.012f, 0.045f, 0.065f, 0.98f);
            Button button = rootObject.GetComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.88f, 1f, 1f);
            colors.pressedColor = new Color(0.70f, 0.90f, 0.92f);
            colors.disabledColor = new Color(0.38f, 0.42f, 0.44f, 0.72f);
            colors.fadeDuration = 0.1f;
            button.colors = colors;

            Image frame = CreateImage(
                root,
                "GeneratedCardFrame",
                Color.white,
                normalCardSprite);
            SetStretch(frame.rectTransform, -4f, -4f, -4f, -4f);
            frame.preserveAspect = true;
            frame.raycastTarget = false;

            Text rarity = CreateText(
                root,
                "Rarity",
                string.Empty,
                17,
                TextAnchor.MiddleCenter,
                Color.white);
            SetAnchoredRect(
                rarity.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -48f),
                new Vector2(180f, 28f));

            Text title = CreateText(
                root,
                "Title",
                string.Empty,
                23,
                TextAnchor.MiddleCenter,
                Color.white);
            SetAnchoredRect(
                title.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -98f),
                new Vector2(242f, 62f));

            Text description = CreateText(
                root,
                "Description",
                string.Empty,
                21,
                TextAnchor.MiddleCenter,
                new Color(0.87f, 0.96f, 1f));
            description.horizontalOverflow = HorizontalWrapMode.Wrap;
            SetAnchoredRect(
                description.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 34f),
                new Vector2(236f, 196f));

            Text owned = CreateText(
                root,
                "OwnedStacks",
                string.Empty,
                15,
                TextAnchor.MiddleCenter,
                new Color(0.65f, 0.76f, 0.8f));
            SetAnchoredRect(
                owned.rectTransform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 102f),
                new Vector2(220f, 30f));

            Text price = CreateText(
                root,
                "Price",
                string.Empty,
                20,
                TextAnchor.MiddleCenter,
                new Color(1f, 0.82f, 0.3f));
            SetAnchoredRect(
                price.rectTransform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 58f),
                new Vector2(232f, 42f));

            int captured = index;
            button.onClick.AddListener(() => SelectOffer(captured));
            return new CardView
            {
                Root = root,
                Group = rootObject.GetComponent<CanvasGroup>(),
                Button = button,
                Frame = frame,
                Rarity = rarity,
                Title = title,
                Description = description,
                Owned = owned,
                Price = price
            };
        }

        GameObject CreateFullscreenDim(Transform parent, string name)
        {
            Image image = CreateImage(
                parent,
                name,
                new Color(0f, 0.015f, 0.025f, 0.78f));
            SetStretch(image.rectTransform, 0f, 0f, 0f, 0f);
            return image.gameObject;
        }

        static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
            {
                return;
            }
            new GameObject(
                "EnhancementEventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
        }

        Image CreateImage(
            Transform parent,
            string name,
            Color color,
            Sprite sprite = null)
        {
            GameObject gameObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image));
            gameObject.transform.SetParent(parent, false);
            Image image = gameObject.GetComponent<Image>();
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
            Color color)
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
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.78f, 0.85f, 0.88f, 1f);
            colors.disabledColor = new Color(0.35f, 0.38f, 0.4f, 0.75f);
            button.colors = colors;
            Text text = CreateText(
                gameObject.transform,
                "Label",
                label,
                20,
                TextAnchor.MiddleCenter,
                Color.white);
            SetStretch(text.rectTransform, 8f, 8f, 4f, 4f);
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
