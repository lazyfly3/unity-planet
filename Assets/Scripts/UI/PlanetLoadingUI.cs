using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlanetLoadingUI : MonoBehaviour
{
    [SerializeField] CanvasGroup canvasGroup;
    [SerializeField] Image progressFill;
    [SerializeField] Text progressText;
    [SerializeField] Text statusText;
    [SerializeField] Text remainingText;

    float startedAt;
    float displayedProgress;
    bool sessionStarted;
    bool simulatingProgress;
    float simulatedFloor;
    float simulatedCeiling;
    float simulatedElapsed;
    string simulatedStatus = string.Empty;
    int simulatedDisplayedPercent = -1;
    int simulatedDisplayedSeconds = -1;

    const float SimulatedProgressTimeConstant = 22f;
    const float SimulatedCeilingMargin = 0.002f;

    public bool IsVisible =>
        gameObject.activeInHierarchy
        && canvasGroup != null
        && canvasGroup.alpha > 0.99f;
    public bool IsComplete { get; private set; }

    public void Show(string initialStatus)
    {
        StopAllCoroutines();
        StopSimulatedProgress();
        gameObject.SetActive(true);

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = short.MaxValue - 1;
        }
        transform.SetAsLastSibling();

        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
        startedAt = Time.realtimeSinceStartup;
        displayedProgress = 0f;
        sessionStarted = true;
        IsComplete = false;
        SetProgress(0.01f, initialStatus);
    }

    public void ShowOrContinue(float progress, string status)
    {
        // The loading canvas is active in the scene asset before a loading
        // session begins, so IsVisible alone cannot tell whether Show has
        // initialized its clock and progress. Only start a new session when
        // necessary; otherwise keep the existing monotonic progress while
        // ownership moves from scene entry to the readiness coordinator.
        if (!sessionStarted || IsComplete || !gameObject.activeSelf)
            Show(status);

        SetProgress(progress, status);
    }

    public void SetProgress(float progress, string status)
    {
        if (canvasGroup == null)
            return;

        progress = Mathf.Clamp01(progress);
        if (simulatingProgress && progress >= simulatedCeiling)
            StopSimulatedProgress();
        displayedProgress = Mathf.Max(displayedProgress, progress);
        progressFill.fillAmount = displayedProgress;
        progressText.text = Mathf.RoundToInt(displayedProgress * 100f) + "%";
        statusText.text = status ?? string.Empty;

        float elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - startedAt);
        if (displayedProgress < 0.04f || elapsed < 0.5f)
        {
            remainingText.text = "正在估算剩余时间…";
            return;
        }

        float remaining = elapsed * (1f - displayedProgress) / displayedProgress;
        remainingText.text = remaining >= 60f
            ? $"预计剩余 {Mathf.CeilToInt(remaining / 60f)} 分钟"
            : $"预计剩余 {Mathf.Max(1, Mathf.CeilToInt(remaining))} 秒";
    }

    /// <summary>
    /// Starts a deliberately non-authoritative waiting animation. It advances
    /// asymptotically and can never claim the next real loading milestone.
    /// </summary>
    public void BeginSimulatedProgress(
        float floor,
        float ceiling,
        string status)
    {
        float safeFloor = Mathf.Clamp01(floor);
        float safeCeiling = Mathf.Clamp01(ceiling);
        if (safeCeiling <= safeFloor + 0.001f)
        {
            StopSimulatedProgress();
            return;
        }
        simulatedFloor = Mathf.Max(displayedProgress, safeFloor);
        simulatedCeiling = safeCeiling;
        simulatedElapsed = 0f;
        simulatedStatus = status ?? string.Empty;
        simulatedDisplayedPercent = -1;
        simulatedDisplayedSeconds = -1;
        simulatingProgress = displayedProgress < simulatedCeiling;
        if (!simulatingProgress)
            return;

        displayedProgress = Mathf.Max(displayedProgress, simulatedFloor);
        progressFill.fillAmount = displayedProgress;
        simulatedDisplayedPercent = Mathf.RoundToInt(
            displayedProgress * 100f);
        progressText.text = simulatedDisplayedPercent + "%";
        statusText.text = simulatedStatus;
        remainingText.text = "城市生成中，已等待 0 秒";
        simulatedDisplayedSeconds = 0;
    }

    public void StopSimulatedProgress()
    {
        simulatingProgress = false;
        simulatedElapsed = 0f;
        simulatedStatus = string.Empty;
        simulatedDisplayedPercent = -1;
        simulatedDisplayedSeconds = -1;
    }

    // Public for deterministic EditMode coverage; the runtime calls it from
    // Update with unscaled delta time.
    public void AdvanceSimulatedProgress(float unscaledDeltaTime)
    {
        if (!simulatingProgress || canvasGroup == null)
            return;

        simulatedElapsed += Mathf.Max(0f, unscaledDeltaTime);
        float range = Mathf.Max(
            0f,
            simulatedCeiling - simulatedFloor -
            SimulatedCeilingMargin);
        float t = 1f - Mathf.Exp(
            -simulatedElapsed / SimulatedProgressTimeConstant);
        float simulated = simulatedFloor + range * Mathf.Clamp01(t);
        displayedProgress = Mathf.Max(displayedProgress, simulated);
        progressFill.fillAmount = displayedProgress;
        int percent = Mathf.RoundToInt(displayedProgress * 100f);
        if (percent != simulatedDisplayedPercent)
        {
            simulatedDisplayedPercent = percent;
            progressText.text = percent + "%";
        }
        int seconds = Mathf.CeilToInt(simulatedElapsed);
        if (seconds != simulatedDisplayedSeconds)
        {
            simulatedDisplayedSeconds = seconds;
            remainingText.text = "城市生成中，已等待 " + seconds + " 秒";
        }
    }

    void Update()
    {
        AdvanceSimulatedProgress(Time.unscaledDeltaTime);
    }

    public void ShowFailure(string status)
    {
        StopSimulatedProgress();
        if (!sessionStarted || IsComplete)
            Show(status);
        else
            StopAllCoroutines();
        gameObject.SetActive(true);
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
        statusText.text = string.IsNullOrWhiteSpace(status)
            ? "星球着陆准备失败"
            : status;
        remainingText.text = "加载已暂停，请查看控制台诊断信息";
        sessionStarted = true;
        IsComplete = false;
    }

    public IEnumerator CompleteAndFade(float duration)
    {
        StopSimulatedProgress();
        SetProgress(1f, "星球着陆准备完成");
        IsComplete = true;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;

        float safeDuration = Mathf.Max(0f, duration);
        float elapsed = 0f;
        while (elapsed < safeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / safeDuration);
            yield return null;
        }

        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        gameObject.SetActive(false);
    }

    public void Complete()
    {
        StopAllCoroutines();
        StopSimulatedProgress();
        SetProgress(1f, "星球着陆准备完成");
        IsComplete = true;
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        gameObject.SetActive(false);
    }

    void OnDisable()
    {
        StopSimulatedProgress();
    }
}
