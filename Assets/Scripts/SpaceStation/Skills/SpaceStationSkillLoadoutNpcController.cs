using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityPlanet.SpaceStation.Enhancement;

namespace UnityPlanet.SpaceStation.Skills
{
    [DisallowMultipleComponent]
    public sealed class SpaceStationSkillLoadoutNpcController : MonoBehaviour
    {
        const string StationSceneName = "SpaceStationUpgradeTest";
        const string NpcObjectName = "NPC_Celeste_Floor01_06_White";
        const float InteractionDistance = 3.2f;

        Transform player;
        SpaceStationFirstPersonController firstPersonController;
        Font font;
        GameObject canvasRoot;
        GameObject promptRoot;
        GameObject interfaceRoot;
        Text walletText;
        Text detailTitle;
        Text detailDescription;
        Text detailLevel;
        Text detailRate;
        Text detailNextEffect;
        Text detailCost;
        Text statusText;
        RawImage detailIcon;
        Button upgradeButton;
        Text upgradeButtonText;
        readonly Dictionary<string, Button> warehouseButtons =
            new Dictionary<string, Button>(StringComparer.Ordinal);
        readonly Dictionary<string, Text> warehouseStatusTexts =
            new Dictionary<string, Text>(StringComparer.Ordinal);
        readonly Button[] slotButtons = new Button[
            PlayerSkillProgressService.SlotCount];
        readonly RawImage[] slotIcons = new RawImage[
            PlayerSkillProgressService.SlotCount];
        readonly Text[] slotNames = new Text[
            PlayerSkillProgressService.SlotCount];
        readonly Text[] slotActions = new Text[
            PlayerSkillProgressService.SlotCount];
        Transform idleChest;
        Transform idleLeftUpperArm;
        Transform idleRightUpperArm;
        Quaternion idleChestRotation;
        Quaternion idleLeftUpperArmRotation;
        Quaternion idleRightUpperArmRotation;
        bool idlePoseReady;
        bool interfaceOpen;
        string selectedSkillId = PlayerSkillCatalog.SelfRepairId;

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
                return;
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform candidate in
                     root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name != NpcObjectName)
                    continue;
                if (candidate.GetComponent<
                        SpaceStationSkillLoadoutNpcController>() == null)
                    candidate.gameObject.AddComponent<
                        SpaceStationSkillLoadoutNpcController>();
                return;
            }
        }

        void Start()
        {
            firstPersonController = FindObjectOfType<
                SpaceStationFirstPersonController>();
            player = firstPersonController != null
                ? firstPersonController.transform
                : null;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ConfigureNaturalIdlePose();
            BuildInterface();
            PlayerSkillProgressService.LoadOrCreate();
            RefreshAll();
            GalaxyCurrencyService.BalanceChanged += HandleBalanceChanged;
            PlayerSkillProgressService.Changed += RefreshAll;
        }

        void Update()
        {
            if (canvasRoot == null)
                return;
            if (!interfaceOpen)
            {
                bool inRange = IsPlayerInRange();
                promptRoot.SetActive(inRange);
                if (inRange && Input.GetKeyDown(KeyCode.F))
                    OpenInterface();
                return;
            }

            promptRoot.SetActive(false);
            if (Input.GetKeyDown(KeyCode.Escape))
                CloseInterface();
        }

        void LateUpdate()
        {
            if (!idlePoseReady)
                return;
            float time = Time.unscaledTime;
            float breath = Mathf.Sin(time * 1.15f);
            idleChest.localRotation = idleChestRotation *
                                      Quaternion.Euler(
                                          breath * 0.45f,
                                          Mathf.Sin(time * 0.53f) * 0.65f,
                                          0f);
            idleLeftUpperArm.localRotation = idleLeftUpperArmRotation *
                                             Quaternion.Euler(
                                                 0f,
                                                 0f,
                                                 breath * 0.28f);
            idleRightUpperArm.localRotation = idleRightUpperArmRotation *
                                              Quaternion.Euler(
                                                  0f,
                                                  0f,
                                                  -breath * 0.22f);
        }

        void ConfigureNaturalIdlePose()
        {
            Animator animator = GetComponent<Animator>();
            if (animator == null ||
                !animator.isHuman ||
                animator.runtimeAnimatorController != null)
                return;

            idleChest = animator.GetBoneTransform(HumanBodyBones.Chest);
            idleLeftUpperArm = animator.GetBoneTransform(
                HumanBodyBones.LeftUpperArm);
            Transform leftLowerArm = animator.GetBoneTransform(
                HumanBodyBones.LeftLowerArm);
            Transform leftHand = animator.GetBoneTransform(
                HumanBodyBones.LeftHand);
            idleRightUpperArm = animator.GetBoneTransform(
                HumanBodyBones.RightUpperArm);
            Transform rightLowerArm = animator.GetBoneTransform(
                HumanBodyBones.RightLowerArm);
            Transform rightHand = animator.GetBoneTransform(
                HumanBodyBones.RightHand);
            if (idleChest == null ||
                idleLeftUpperArm == null ||
                leftLowerArm == null ||
                leftHand == null ||
                idleRightUpperArm == null ||
                rightLowerArm == null ||
                rightHand == null)
                return;

            Vector3 right = transform.right;
            Vector3 forward = transform.forward;
            AimBoneToward(
                idleLeftUpperArm,
                leftLowerArm,
                -right * 0.12f + Vector3.down * 0.99f + forward * 0.05f);
            AimBoneToward(
                leftLowerArm,
                leftHand,
                right * 0.03f + Vector3.down * 0.98f + forward * 0.18f);
            AimBoneToward(
                idleRightUpperArm,
                rightLowerArm,
                right * 0.15f + Vector3.down * 0.98f - forward * 0.08f);
            AimBoneToward(
                rightLowerArm,
                rightHand,
                -right * 0.42f + Vector3.down * 0.8f + forward * 0.42f);

            idleChestRotation = idleChest.localRotation;
            idleLeftUpperArmRotation = idleLeftUpperArm.localRotation;
            idleRightUpperArmRotation = idleRightUpperArm.localRotation;
            idlePoseReady = true;
        }

        static void AimBoneToward(
            Transform bone,
            Transform child,
            Vector3 worldDirection)
        {
            Vector3 currentDirection = child.position - bone.position;
            if (currentDirection.sqrMagnitude < 0.000001f ||
                worldDirection.sqrMagnitude < 0.000001f)
                return;
            bone.rotation = Quaternion.FromToRotation(
                                currentDirection.normalized,
                                worldDirection.normalized) *
                            bone.rotation;
        }

        void OnDestroy()
        {
            GalaxyCurrencyService.BalanceChanged -= HandleBalanceChanged;
            PlayerSkillProgressService.Changed -= RefreshAll;
            if (canvasRoot != null)
                Destroy(canvasRoot);
            if (interfaceOpen && firstPersonController != null)
                firstPersonController.enabled = true;
        }

        bool IsPlayerInRange()
        {
            if (player == null)
                return false;
            Vector3 offset = player.position - transform.position;
            offset.y *= 0.35f;
            return offset.sqrMagnitude <=
                   InteractionDistance * InteractionDistance;
        }

        void OpenInterface()
        {
            interfaceOpen = true;
            interfaceRoot.SetActive(true);
            promptRoot.SetActive(false);
            if (firstPersonController != null)
                firstPersonController.enabled = false;
            EnsureEventSystem();
            RefreshAll();
            statusText.text =
                "从技能仓库选择技能，再点击 1 / 2 / 3 装载槽。再次点击已装载技能可卸载。";
        }

        void CloseInterface()
        {
            interfaceOpen = false;
            interfaceRoot.SetActive(false);
            if (firstPersonController != null)
                firstPersonController.enabled = true;
        }

        void SelectSkill(string skillId)
        {
            selectedSkillId = skillId;
            RefreshAll();
            statusText.text = "已选择技能，点击空槽进行装载。";
        }

        void HandleSlotClicked(int slotIndex)
        {
            PlayerSkillProgressData data =
                PlayerSkillProgressService.LoadOrCreate();
            string equipped = data.equippedSkillIds[slotIndex];
            bool same = string.Equals(
                equipped,
                selectedSkillId,
                StringComparison.Ordinal);
            bool success = same
                ? PlayerSkillProgressService.TryUnequip(
                    slotIndex,
                    out string message)
                : PlayerSkillProgressService.TryEquip(
                    selectedSkillId,
                    slotIndex,
                    out message);
            statusText.text = message;
            if (success)
                RefreshAll();
        }

        void UpgradeSelected()
        {
            PlayerSkillProgressService.TryUpgrade(
                selectedSkillId,
                out string message);
            statusText.text = message;
            RefreshAll();
        }

        void RefreshAll()
        {
            if (walletText == null)
                return;
            PlayerSkillProgressData data =
                PlayerSkillProgressService.LoadOrCreate();
            GalaxyEnhancementProgressData currency =
                GalaxyCurrencyService.LoadOrCreate();
            walletText.text = currency.galaxyCoins.ToString("N0") + "  银河币";

            foreach (PlayerSkillDefinition warehouseDefinition in
                     PlayerSkillCatalog.All)
            {
                PlayerSkillProgressEntry warehouseEntry =
                    PlayerSkillProgressService.Find(
                        data,
                        warehouseDefinition.Id);
                bool owned = warehouseEntry != null;
                if (warehouseButtons.TryGetValue(
                        warehouseDefinition.Id,
                        out Button warehouseButton))
                    warehouseButton.interactable = owned;
                if (warehouseStatusTexts.TryGetValue(
                        warehouseDefinition.Id,
                        out Text ownedText))
                {
                    if (!owned)
                        ownedText.text = "尚未获得  ·  金卡技能";
                    else if (string.Equals(
                                 warehouseDefinition.Id,
                                 PlayerSkillCatalog.SelfRepairId,
                                 StringComparison.Ordinal))
                        ownedText.text = "默认拥有  ·  Lv." +
                                         warehouseEntry.level;
                    else
                        ownedText.text = "已拥有  ·  Lv." +
                                         warehouseEntry.level;
                }
            }

            for (int index = 0; index < slotButtons.Length; index++)
            {
                string id = data.equippedSkillIds[index];
                if (!PlayerSkillCatalog.TryGet(
                        id,
                        out PlayerSkillDefinition slotDefinition))
                {
                    slotNames[index].text = "空装载槽";
                    slotActions[index].text = "装载所选技能";
                    slotIcons[index].texture = null;
                    slotIcons[index].color = new Color(1f, 1f, 1f, 0.1f);
                    continue;
                }

                PlayerSkillProgressEntry slotEntry =
                    PlayerSkillProgressService.Find(data, id);
                slotNames[index].text = slotDefinition.DisplayName +
                                        "  Lv." + (slotEntry?.level ?? 1);
                slotActions[index].text = id == selectedSkillId
                    ? "点击卸载"
                    : "点击替换";
                slotIcons[index].texture = Resources.Load<Texture2D>(
                    slotDefinition.IconResourcePath);
                slotIcons[index].color = Color.white;
            }

            if (!PlayerSkillCatalog.TryGet(
                    selectedSkillId,
                    out PlayerSkillDefinition definition))
                return;
            PlayerSkillProgressEntry entry =
                PlayerSkillProgressService.Find(data, selectedSkillId);
            int level = entry?.level ?? 1;
            detailTitle.text = definition.DisplayName;
            detailDescription.text = definition.Description;
            detailLevel.text = "技能等级  " + LevelPips(
                level,
                definition.MaximumLevel) + "  Lv." + level;
            detailRate.text = "当前效果  " +
                              PlayerSkillCatalog.EffectForLevel(
                                  definition.Id,
                                  level);
            detailIcon.texture = Resources.Load<Texture2D>(
                definition.IconResourcePath);
            if (level >= definition.MaximumLevel)
            {
                detailNextEffect.text = "下一级  已达到最高等级";
                detailCost.text = "技能已达到最高等级";
                upgradeButtonText.text = "已满级";
                upgradeButton.interactable = false;
            }
            else
            {
                int cost = definition.UpgradeCostFromLevel(level);
                detailNextEffect.text = "下一级 Lv." + (level + 1) + "  " +
                                        PlayerSkillCatalog.EffectForLevel(
                                            definition.Id,
                                            level + 1);
                detailCost.text = "升级消耗  " + cost.ToString("N0") +
                                  " 银河币";
                upgradeButtonText.text = "升级到 Lv." + (level + 1);
                upgradeButton.interactable = currency.galaxyCoins >= cost;
            }
        }

        void HandleBalanceChanged(int balance)
        {
            RefreshAll();
        }

        void BuildInterface()
        {
            canvasRoot = new GameObject(
                "SkillLoadoutNpcCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 520;
            CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            promptRoot = CreatePanel(
                "Prompt",
                canvasRoot.transform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(520f, 62f),
                new Vector2(0f, 68f),
                new Color(0.01f, 0.07f, 0.1f, 0.94f),
                new Color(0.05f, 0.86f, 1f, 0.9f)).gameObject;
            Text prompt = CreateText(
                "PromptText",
                promptRoot.transform,
                "F   与技能装载师交谈",
                25,
                TextAnchor.MiddleCenter,
                Color.white);
            Stretch(prompt.rectTransform, 12f);

            RectTransform dim = CreatePanel(
                "Interface",
                canvasRoot.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                new Color(0f, 0.015f, 0.028f, 0.86f),
                Color.clear);
            interfaceRoot = dim.gameObject;

            RectTransform main = CreatePanel(
                "MainPanel",
                dim,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(1540f, 850f),
                Vector2.zero,
                new Color(0.008f, 0.04f, 0.075f, 0.98f),
                new Color(0.04f, 0.74f, 0.9f, 0.95f));

            RawImage generatedBackdrop = CreateRawImage(
                "GeneratedBackdrop",
                main,
                Vector2.zero,
                new Vector2(1540f, 850f));
            generatedBackdrop.texture = Resources.Load<Texture2D>(
                "UI/Skills/SkillLoadoutBackdrop");
            generatedBackdrop.color = generatedBackdrop.texture != null
                ? Color.white
                : Color.clear;
            generatedBackdrop.transform.SetAsFirstSibling();

            Text title = CreateText(
                "Title",
                main,
                "技能装载",
                42,
                TextAnchor.MiddleLeft,
                new Color(0.35f, 0.95f, 1f));
            SetRect(title.rectTransform, new Vector2(48f, 776f),
                new Vector2(500f, 54f));
            walletText = CreateText(
                "Wallet",
                main,
                string.Empty,
                27,
                TextAnchor.MiddleRight,
                new Color(1f, 0.82f, 0.3f));
            SetRect(walletText.rectTransform, new Vector2(1120f, 782f),
                new Vector2(360f, 44f));

            BuildWarehouse(main);
            BuildSlots(main);
            BuildDetail(main);

            statusText = CreateText(
                "Status",
                main,
                string.Empty,
                22,
                TextAnchor.MiddleLeft,
                new Color(0.46f, 0.91f, 1f));
            SetRect(statusText.rectTransform, new Vector2(52f, 54f),
                new Vector2(1230f, 38f));
            Text closeHint = CreateText(
                "CloseHint",
                main,
                "ESC  返回",
                21,
                TextAnchor.MiddleRight,
                new Color(0.72f, 0.84f, 0.9f));
            SetRect(closeHint.rectTransform, new Vector2(1290f, 54f),
                new Vector2(190f, 38f));

            interfaceRoot.SetActive(false);
            promptRoot.SetActive(false);
        }

        void BuildWarehouse(RectTransform main)
        {
            RectTransform panel = CreatePanel(
                "Warehouse",
                main,
                Vector2.zero,
                Vector2.zero,
                new Vector2(410f, 660f),
                new Vector2(52f, 100f),
                new Color(0.01f, 0.075f, 0.115f, 0.28f),
                Color.clear);
            Text header = CreateText(
                "Header",
                panel,
                "技能仓库",
                28,
                TextAnchor.MiddleLeft,
                new Color(0.35f, 0.94f, 1f));
            SetRect(header.rectTransform, new Vector2(26f, 596f),
                new Vector2(350f, 44f));

            RectTransform scrollRoot = CreatePanel(
                "WarehouseScroll",
                panel,
                Vector2.zero,
                Vector2.zero,
                new Vector2(374f, 552f),
                new Vector2(18f, 24f),
                new Color(0f, 0.025f, 0.045f, 0.72f),
                Color.clear);
            ScrollRect scroll = scrollRoot.gameObject.AddComponent<
                ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.scrollSensitivity = 34f;

            RectTransform viewport = CreatePanel(
                "Viewport",
                scrollRoot,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                new Color(0f, 0f, 0f, 0.01f),
                Color.clear);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport;

            GameObject contentObject = new GameObject(
                "Content",
                typeof(RectTransform));
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            const float cardHeight = 104f;
            const float cardSpacing = 12f;
            float contentHeight = 12f + PlayerSkillCatalog.All.Count *
                (cardHeight + cardSpacing);
            content.sizeDelta = new Vector2(0f, contentHeight);
            content.anchoredPosition = Vector2.zero;
            scroll.content = content;

            int cardIndex = 0;
            foreach (PlayerSkillDefinition definition in
                     PlayerSkillCatalog.All)
            {
                string capturedSkillId = definition.Id;
                float y = contentHeight - 12f - cardHeight -
                          cardIndex * (cardHeight + cardSpacing);
                Button card = CreateButton(
                    "SkillCard_" + cardIndex,
                    content,
                    new Vector2(8f, y),
                    new Vector2(350f, cardHeight),
                    new Color(0.018f, 0.13f, 0.18f, 1f),
                    new Color(0.1f, 0.94f, 1f),
                    () => SelectSkill(capturedSkillId));
                warehouseButtons[capturedSkillId] = card;
                RawImage icon = CreateRawImage(
                    "Icon",
                    card.transform,
                    new Vector2(14f, 14f),
                    new Vector2(76f, 76f));
                icon.texture = Resources.Load<Texture2D>(
                    definition.IconResourcePath);
                Text name = CreateText(
                    "Name",
                    card.transform,
                    definition.DisplayName,
                    22,
                    TextAnchor.MiddleLeft,
                    Color.white);
                SetRect(name.rectTransform, new Vector2(104f, 55f),
                    new Vector2(224f, 34f));
                Text owned = CreateText(
                    "Owned",
                    card.transform,
                    string.Empty,
                    16,
                    TextAnchor.MiddleLeft,
                    new Color(0.35f, 0.91f, 1f));
                SetRect(owned.rectTransform, new Vector2(104f, 20f),
                    new Vector2(226f, 28f));
                warehouseStatusTexts[capturedSkillId] = owned;
                cardIndex++;
            }
            scroll.verticalNormalizedPosition = 1f;
        }

        void BuildSlots(RectTransform main)
        {
            RectTransform panel = CreatePanel(
                "Loadout",
                main,
                Vector2.zero,
                Vector2.zero,
                new Vector2(570f, 660f),
                new Vector2(482f, 100f),
                new Color(0.006f, 0.045f, 0.078f, 0.2f),
                Color.clear);
            Text header = CreateText(
                "Header",
                panel,
                "1 / 2 / 3  战斗技能装载槽",
                27,
                TextAnchor.MiddleCenter,
                new Color(0.35f, 0.94f, 1f));
            SetRect(header.rectTransform, new Vector2(25f, 596f),
                new Vector2(520f, 44f));

            for (int index = 0; index < slotButtons.Length; index++)
            {
                int captured = index;
                float x = 24f + index * 178f;
                Button button = CreateButton(
                    "Slot" + (index + 1),
                    panel,
                    new Vector2(x, 198f),
                    new Vector2(166f, 360f),
                    new Color(0.012f, 0.085f, 0.125f, 1f),
                    new Color(0.06f, 0.75f, 0.92f),
                    () => HandleSlotClicked(captured));
                slotButtons[index] = button;
                Text key = CreateText(
                    "Key",
                    button.transform,
                    (index + 1).ToString(),
                    42,
                    TextAnchor.MiddleCenter,
                    new Color(1f, 0.82f, 0.3f));
                SetRect(key.rectTransform, new Vector2(20f, 286f),
                    new Vector2(126f, 56f));
                slotIcons[index] = CreateRawImage(
                    "Icon",
                    button.transform,
                    new Vector2(27f, 130f),
                    new Vector2(112f, 112f));
                slotNames[index] = CreateText(
                    "SkillName",
                    button.transform,
                    "空装载槽",
                    19,
                    TextAnchor.MiddleCenter,
                    Color.white);
                SetRect(slotNames[index].rectTransform,
                    new Vector2(8f, 78f), new Vector2(150f, 40f));
                slotActions[index] = CreateText(
                    "Action",
                    button.transform,
                    "装载所选技能",
                    16,
                    TextAnchor.MiddleCenter,
                    new Color(0.35f, 0.91f, 1f));
                SetRect(slotActions[index].rectTransform,
                    new Vector2(8f, 28f), new Vector2(150f, 34f));
            }
        }

        void BuildDetail(RectTransform main)
        {
            RectTransform panel = CreatePanel(
                "Detail",
                main,
                Vector2.zero,
                Vector2.zero,
                new Vector2(448f, 660f),
                new Vector2(1072f, 100f),
                new Color(0.01f, 0.065f, 0.102f, 0.22f),
                Color.clear);

            detailTitle = CreateText(
                "Title",
                panel,
                string.Empty,
                32,
                TextAnchor.MiddleCenter,
                Color.white);
            SetRect(detailTitle.rectTransform, new Vector2(29f, 600f),
                new Vector2(390f, 44f));

            detailIcon = CreateRawImage(
                "Icon",
                panel,
                new Vector2(160f, 442f),
                new Vector2(128f, 128f));
            detailDescription = CreateText(
                "Description",
                panel,
                string.Empty,
                18,
                TextAnchor.UpperLeft,
                new Color(0.78f, 0.9f, 0.95f));
            detailDescription.horizontalOverflow = HorizontalWrapMode.Wrap;
            detailDescription.verticalOverflow = VerticalWrapMode.Truncate;
            detailDescription.resizeTextForBestFit = true;
            detailDescription.resizeTextMinSize = 14;
            detailDescription.resizeTextMaxSize = 18;
            SetRect(detailDescription.rectTransform, new Vector2(35f, 346f),
                new Vector2(378f, 88f));

            detailLevel = CreateText(
                "Level",
                panel,
                string.Empty,
                21,
                TextAnchor.MiddleLeft,
                new Color(0.4f, 0.94f, 1f));
            SetRect(detailLevel.rectTransform, new Vector2(35f, 296f),
                new Vector2(378f, 38f));

            detailRate = CreateText(
                "Rate",
                panel,
                string.Empty,
                18,
                TextAnchor.MiddleLeft,
                Color.white);
            detailRate.horizontalOverflow = HorizontalWrapMode.Wrap;
            detailRate.verticalOverflow = VerticalWrapMode.Truncate;
            SetRect(detailRate.rectTransform, new Vector2(35f, 244f),
                new Vector2(378f, 42f));

            detailNextEffect = CreateText(
                "NextEffect",
                panel,
                string.Empty,
                18,
                TextAnchor.MiddleLeft,
                new Color(1f, 0.82f, 0.3f));
            detailNextEffect.horizontalOverflow = HorizontalWrapMode.Wrap;
            detailNextEffect.verticalOverflow = VerticalWrapMode.Truncate;
            detailNextEffect.resizeTextForBestFit = true;
            detailNextEffect.resizeTextMinSize = 15;
            detailNextEffect.resizeTextMaxSize = 18;
            SetRect(detailNextEffect.rectTransform, new Vector2(35f, 184f),
                new Vector2(378f, 52f));

            detailCost = CreateText(
                "Cost",
                panel,
                string.Empty,
                19,
                TextAnchor.MiddleLeft,
                new Color(1f, 0.82f, 0.3f));
            SetRect(detailCost.rectTransform, new Vector2(35f, 124f),
                new Vector2(378f, 42f));
            upgradeButton = CreateButton(
                "Upgrade",
                panel,
                new Vector2(29f, 34f),
                new Vector2(390f, 76f),
                new Color(0.24f, 0.15f, 0.035f, 0.28f),
                Color.clear,
                UpgradeSelected);
            upgradeButtonText = CreateText(
                "Text",
                upgradeButton.transform,
                "升级",
                28,
                TextAnchor.MiddleCenter,
                Color.white);
            Stretch(upgradeButtonText.rectTransform, 6f);
        }

        Button CreateButton(
            string name,
            Transform parent,
            Vector2 position,
            Vector2 size,
            Color color,
            Color outlineColor,
            UnityEngine.Events.UnityAction action)
        {
            RectTransform rect = CreatePanel(
                name,
                parent,
                Vector2.zero,
                Vector2.zero,
                size,
                position,
                color,
                outlineColor);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(action);
            ColorBlock colors = button.colors;
            colors.highlightedColor = color * 1.35f;
            colors.pressedColor = color * 0.72f;
            colors.disabledColor = new Color(0.08f, 0.1f, 0.12f, 0.72f);
            button.colors = colors;
            return button;
        }

        RectTransform CreatePanel(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 size,
            Vector2 position,
            Color color,
            Color outlineColor)
        {
            GameObject gameObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image));
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = anchorMin == Vector2.zero && anchorMax == Vector2.zero
                ? Vector2.zero
                : new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            if (anchorMin == Vector2.zero && anchorMax == Vector2.one)
            {
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            Image image = gameObject.GetComponent<Image>();
            image.color = color;
            if (outlineColor.a > 0f)
            {
                Outline outline = gameObject.AddComponent<Outline>();
                outline.effectColor = outlineColor;
                outline.effectDistance = new Vector2(2f, -2f);
            }
            return rect;
        }

        Text CreateText(
            string name,
            Transform parent,
            string value,
            int size,
            TextAnchor alignment,
            Color color)
        {
            GameObject gameObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Text));
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Text text = gameObject.GetComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            return text;
        }

        static RawImage CreateRawImage(
            string name,
            Transform parent,
            Vector2 position,
            Vector2 size)
        {
            GameObject gameObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(RawImage));
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            SetRect(rect, position, size);
            RawImage image = gameObject.GetComponent<RawImage>();
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        static void SetRect(
            RectTransform rect,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * padding;
            rect.offsetMax = -Vector2.one * padding;
        }

        static string LevelPips(int level, int maximum)
        {
            return new string('◆', Mathf.Clamp(level, 0, maximum)) +
                   new string('◇', Mathf.Max(0, maximum - level));
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;
            new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
        }
    }
}
