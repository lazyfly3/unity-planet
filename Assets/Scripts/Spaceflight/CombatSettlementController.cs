using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum CombatSettlementOutcome
{
    Victory,
    Defeat,
    VoluntaryReturn
}

[Serializable]
public sealed class CombatSettlementData
{
    public CombatSettlementOutcome outcome;
    public string planetId;
    public string missionId;
    public string missionName;
    public int planetDifficultyIndex;
    public int kills;
    public int objectivesCompleted;
    public int objectiveCount;
    public int objectiveScorePerItem;
    public int bossDamagePercent;
    public int participationScore;
    public int killScore;
    public int objectiveScore;
    public int resultBonusScore;
    public int totalScore;
    public int galaxyCoinReward;
    public string objectiveBreakdown;

    public bool IsVictory =>
        outcome == CombatSettlementOutcome.Victory;
}

public static class CombatSettlementCalculator
{
    public const int ParticipationScore = 100;
    public const int ScorePerKill = 120;
    public const int ScorePerSurveyBeacon = 300;
    public const int ScorePerEnergyCore = 450;
    public const int MaximumBossDamageScore = 1200;
    public const int VictoryBonusScore = 1000;
    public const float ScorePerGalaxyCoin = 18f;

    public static CombatSettlementData Calculate(
        CombatSettlementOutcome outcome,
        string planetId,
        string missionId,
        string missionName,
        FinitePlanetMissionRules rules,
        int kills,
        int objectivesCompleted,
        float bossDamageRatio)
    {
        if (rules == null)
            throw new ArgumentNullException(nameof(rules));

        int safeKills = Mathf.Max(0, kills);
        int safeObjectives = Mathf.Clamp(
            objectivesCompleted,
            0,
            Mathf.Max(0, rules.ObjectiveCount));
        int bossDamagePercent = Mathf.RoundToInt(
            Mathf.Clamp01(bossDamageRatio) * 100f);
        int objectiveUnit = 0;
        int objectiveScore = 0;
        string objectiveBreakdown;
        switch (rules.Kind)
        {
            case FinitePlanetMissionObjectiveKind.Survey:
                objectiveUnit = ScorePerSurveyBeacon;
                objectiveScore = safeObjectives * objectiveUnit;
                objectiveBreakdown =
                    "扫描信标  " + safeObjectives + "/" +
                    rules.ObjectiveCount + "  ·  +" + objectiveScore;
                break;
            case FinitePlanetMissionObjectiveKind.Assault:
                objectiveUnit = ScorePerEnergyCore;
                objectiveScore = safeObjectives * objectiveUnit;
                objectiveBreakdown =
                    "能源核心  " + safeObjectives + "/" +
                    rules.ObjectiveCount + "  ·  +" + objectiveScore;
                break;
            case FinitePlanetMissionObjectiveKind.Boss:
                objectiveScore = Mathf.RoundToInt(
                    MaximumBossDamageScore * bossDamagePercent / 100f);
                objectiveBreakdown =
                    "首领结构伤害  " + bossDamagePercent + "%  ·  +" +
                    objectiveScore;
                break;
            default:
                objectiveBreakdown = "区域目标  敌机清剿";
                break;
        }

        int participation = ParticipationScore;
        int killScore = safeKills * ScorePerKill;
        int resultBonus = outcome == CombatSettlementOutcome.Victory
            ? VictoryBonusScore
            : 0;
        int total = checked(
            participation + killScore + objectiveScore + resultBonus);
        float difficultyMultiplier = 1f +
            Mathf.Clamp(rules.PlanetDifficultyIndex, 0, 5) * 0.08f;
        int reward = Mathf.Max(
            8,
            Mathf.RoundToInt(
                total / ScorePerGalaxyCoin * difficultyMultiplier));
        if (outcome == CombatSettlementOutcome.Victory)
            reward = Mathf.Max(reward, rules.GalaxyCoinReward);

        return new CombatSettlementData
        {
            outcome = outcome,
            planetId = planetId ?? string.Empty,
            missionId = missionId ?? string.Empty,
            missionName = string.IsNullOrWhiteSpace(missionName)
                ? "星球战斗任务"
                : missionName,
            planetDifficultyIndex = rules.PlanetDifficultyIndex,
            kills = safeKills,
            objectivesCompleted = safeObjectives,
            objectiveCount = Mathf.Max(0, rules.ObjectiveCount),
            objectiveScorePerItem = objectiveUnit,
            bossDamagePercent = bossDamagePercent,
            participationScore = participation,
            killScore = killScore,
            objectiveScore = objectiveScore,
            resultBonusScore = resultBonus,
            totalScore = total,
            galaxyCoinReward = reward,
            objectiveBreakdown = objectiveBreakdown
        };
    }
}

[DisallowMultipleComponent]
public sealed class CombatSettlementController : MonoBehaviour
{
    static readonly Color Cyan =
        new Color(0.12f, 0.9f, 0.94f, 1f);
    static readonly Color TextPrimary =
        new Color(0.84f, 0.96f, 1f, 1f);
    static readonly Color Orange =
        new Color(1f, 0.55f, 0.15f, 1f);
    static readonly Color Red =
        new Color(1f, 0.32f, 0.28f, 1f);

    CombatSettlementData data;
    Action returnAction;
    Canvas canvas;
    Text scoreValue;
    Text rewardValue;
    Button returnButton;
    Coroutine animationRoutine;
    float previousTimeScale = 1f;
    bool animationComplete;

    public bool AnimationComplete => animationComplete;
    public int DisplayedScore { get; private set; }
    public int DisplayedReward { get; private set; }

    public static CombatSettlementController Show(
        CombatSettlementData settlement,
        Action onReturnToStation)
    {
        CombatSettlementController existing =
            FindObjectOfType<CombatSettlementController>();
        if (existing != null)
            return existing;

        GameObject root = new GameObject(
            "CombatSettlementController");
        CombatSettlementController controller =
            root.AddComponent<CombatSettlementController>();
        controller.Initialize(settlement, onReturnToStation);
        return controller;
    }

    void Initialize(
        CombatSettlementData settlement,
        Action onReturnToStation)
    {
        data = settlement ??
            throw new ArgumentNullException(nameof(settlement));
        returnAction = onReturnToStation;
        previousTimeScale = Time.timeScale > 0f
            ? Time.timeScale
            : 1f;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        BuildInterface();
        animationRoutine = StartCoroutine(PlayNumberAnimation());
    }

    void Update()
    {
        if (animationComplete &&
            (Input.GetKeyDown(KeyCode.Return) ||
             Input.GetKeyDown(KeyCode.KeypadEnter) ||
             Input.GetKeyDown(KeyCode.Space)))
        {
            ReturnToStation();
        }
    }

    void BuildInterface()
    {
        if (EventSystem.current == null)
        {
            GameObject eventObject = new GameObject(
                "CombatSettlementEventSystem");
            eventObject.AddComponent<EventSystem>();
            eventObject.AddComponent<StandaloneInputModule>();
        }

        GameObject canvasObject = new GameObject(
            "CombatSettlementCanvas");
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        // The backdrop and all coordinates are authored in one 16:9 design
        // space. Expand keeps that 1920x1080 space stable at every aspect ratio,
        // so title, row values and buttons cannot drift past their artwork.
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject backdropObject = new GameObject(
            "SettlementFrame",
            typeof(RectTransform),
            typeof(Image),
            typeof(AspectRatioFitter));
        backdropObject.transform.SetParent(canvas.transform, false);
        SetStretch(backdropObject.GetComponent<RectTransform>());
        AspectRatioFitter backdropFitter =
            backdropObject.GetComponent<AspectRatioFitter>();
        backdropFitter.aspectMode =
            AspectRatioFitter.AspectMode.FitInParent;
        backdropFitter.aspectRatio = 1672f / 941f;
        Image backdrop = backdropObject.GetComponent<Image>();
        backdrop.sprite = Resources.Load<Sprite>(
            "UI/CombatSettlement/CombatSettlementBackdrop");
        backdrop.color = Color.white;
        backdrop.preserveAspect = false;

        GameObject contentObject = new GameObject(
            "SettlementContent",
            typeof(RectTransform));
        contentObject.transform.SetParent(backdropObject.transform, false);
        RectTransform contentRect =
            contentObject.GetComponent<RectTransform>();
        SetStretch(contentRect);
        // The line-art rows in the authored frame sit lower than the original
        // runtime coordinates. Move the complete information block as one unit
        // so title, totals and row text retain their internal spacing while all
        // baselines land inside their intended compartments.
        contentRect.anchoredPosition = new Vector2(0f, -36f);
        Transform content = contentObject.transform;

        Color outcomeColor = data.outcome ==
            CombatSettlementOutcome.Victory
                ? Orange
                : data.outcome == CombatSettlementOutcome.Defeat
                    ? Red
                    : Cyan;
        Text outcome = CreateText(
            content,
            data.outcome == CombatSettlementOutcome.Victory
                ? "任务完成"
                : data.outcome == CombatSettlementOutcome.Defeat
                    ? "飞船损毁"
                    : "战术返航",
            50,
            TextAnchor.MiddleCenter,
            outcomeColor);
        SetRect(outcome.rectTransform,
            new Vector2(0f, 438f), new Vector2(760f, 72f));
        ConfigureBestFit(outcome, 28, 50);

        Text mission = CreateText(
            content,
            data.missionName,
            31,
            TextAnchor.MiddleCenter,
            TextPrimary);
        SetRect(mission.rectTransform,
            new Vector2(0f, 298f), new Vector2(920f, 54f));
        ConfigureBestFit(mission, 20, 31);

        Text scoreLabel = CreateText(
            content,
            "战果积分",
            27,
            TextAnchor.MiddleCenter,
            new Color(0.55f, 0.8f, 0.86f, 1f));
        SetRect(scoreLabel.rectTransform,
            new Vector2(-250f, 205f), new Vector2(360f, 46f));
        scoreValue = CreateText(
            content,
            "0",
            72,
            TextAnchor.MiddleCenter,
            TextPrimary);
        SetRect(scoreValue.rectTransform,
            new Vector2(-250f, 116f), new Vector2(440f, 108f));
        ConfigureBestFit(scoreValue, 40, 72);

        Text rewardLabel = CreateText(
            content,
            "银河币奖励",
            27,
            TextAnchor.MiddleCenter,
            new Color(0.55f, 0.8f, 0.86f, 1f));
        SetRect(rewardLabel.rectTransform,
            new Vector2(250f, 205f), new Vector2(360f, 46f));
        rewardValue = CreateText(
            content,
            "+0",
            64,
            TextAnchor.MiddleCenter,
            Orange);
        SetRect(rewardValue.rectTransform,
            new Vector2(250f, 116f), new Vector2(440f, 108f));
        ConfigureBestFit(rewardValue, 36, 64);

        CreateDetailRow(
            content,
            0,
            "参与战斗",
            "+" + data.participationScore);
        CreateDetailRow(
            content,
            1,
            "有效击落  " + data.kills + " × " +
            CombatSettlementCalculator.ScorePerKill,
            "+" + data.killScore);
        CreateDetailRow(
            content,
            2,
            data.objectiveBreakdown,
            data.resultBonusScore > 0
                ? "完成加成  +" + data.resultBonusScore
                : "战果已保留");

        returnButton = CreateButton(
            backdropObject.transform,
            "返回空间站",
            new Vector2(0f, -406f),
            new Vector2(430f, 70f));
        returnButton.interactable = false;
        returnButton.onClick.AddListener(ReturnToStation);
    }

    void CreateDetailRow(
        Transform parent,
        int index,
        string leftValue,
        string rightValue)
    {
        float y = -82f - index * 91f;
        Text left = CreateText(
            parent,
            leftValue,
            27,
            TextAnchor.MiddleLeft,
            TextPrimary);
        SetRect(left.rectTransform,
            new Vector2(-180f, y), new Vector2(700f, 54f));
        ConfigureBestFit(left, 17, 27);
        Text right = CreateText(
            parent,
            rightValue,
            27,
            TextAnchor.MiddleRight,
            index == 2 && data.resultBonusScore == 0
                ? new Color(0.55f, 0.8f, 0.86f, 1f)
                : Orange);
        SetRect(right.rectTransform,
            new Vector2(430f, y), new Vector2(360f, 54f));
        ConfigureBestFit(right, 17, 27);
    }

    IEnumerator PlayNumberAnimation()
    {
        const float duration = 1.8f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float linear = Mathf.Clamp01(elapsed / duration);
            float progress = 1f - Mathf.Pow(1f - linear, 3f);
            DisplayedScore = Mathf.RoundToInt(data.totalScore * progress);
            DisplayedReward = Mathf.RoundToInt(
                data.galaxyCoinReward * progress);
            scoreValue.text = DisplayedScore.ToString("N0");
            rewardValue.text = "+" + DisplayedReward.ToString("N0");
            yield return null;
        }

        DisplayedScore = data.totalScore;
        DisplayedReward = data.galaxyCoinReward;
        scoreValue.text = DisplayedScore.ToString("N0");
        rewardValue.text = "+" + DisplayedReward.ToString("N0");
        animationComplete = true;
        returnButton.interactable = true;
        Text label = returnButton.GetComponentInChildren<Text>(true);
        if (label != null)
            label.text = "返回空间站  Enter";
        animationRoutine = null;
    }

    public void ReturnToStation()
    {
        if (!animationComplete)
            return;
        animationComplete = false;
        returnButton.interactable = false;
        Time.timeScale = previousTimeScale;
        Action callback = returnAction;
        returnAction = null;
        callback?.Invoke();
    }

    void OnDestroy()
    {
        if (animationRoutine != null)
            StopCoroutine(animationRoutine);
        if (Time.timeScale <= 0f)
            Time.timeScale = previousTimeScale;
    }

    static Text CreateText(
        Transform parent,
        string value,
        int fontSize,
        TextAnchor alignment,
        Color color)
    {
        GameObject textObject = new GameObject(
            "Text",
            typeof(RectTransform),
            typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>(
            "LegacyRuntime.ttf");
        text.text = value ?? string.Empty;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    static Button CreateButton(
        Transform parent,
        string label,
        Vector2 position,
        Vector2 size)
    {
        GameObject buttonObject = new GameObject(
            "ReturnStationButton",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        SetRect(buttonObject.GetComponent<RectTransform>(), position, size);
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.025f, 0.21f, 0.27f, 0.96f);
        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = Cyan;
        outline.effectDistance = new Vector2(1.4f, -1.4f);
        Button button = buttonObject.GetComponent<Button>();
        Text text = CreateText(
            buttonObject.transform,
            label,
            25,
            TextAnchor.MiddleCenter,
            TextPrimary);
        SetStretch(text.rectTransform, new Vector2(18f, 8f));
        ConfigureBestFit(text, 17, 25);
        return button;
    }

    static void ConfigureBestFit(
        Text text,
        int minimum,
        int maximum)
    {
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = minimum;
        text.resizeTextMaxSize = maximum;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
    }

    static void SetRect(
        RectTransform rect,
        Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    static void SetStretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static void SetStretch(RectTransform rect, Vector2 inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = inset;
        rect.offsetMax = -inset;
    }
}
