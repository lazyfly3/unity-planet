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

    public bool IsVisible =>
        gameObject.activeInHierarchy
        && canvasGroup != null
        && canvasGroup.alpha > 0.99f;
    public bool IsComplete { get; private set; }

    public void Show(string initialStatus)
    {
        StopAllCoroutines();
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
        IsComplete = false;
        SetProgress(0.01f, initialStatus);
    }

    public void SetProgress(float progress, string status)
    {
        if (canvasGroup == null)
            return;

        progress = Mathf.Clamp01(progress);
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

    public void ShowFailure(string status)
    {
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
        statusText.text = string.IsNullOrWhiteSpace(status)
            ? "星球着陆准备失败"
            : status;
        remainingText.text = "加载已暂停，请查看控制台诊断信息";
        IsComplete = false;
    }

    public IEnumerator CompleteAndFade(float duration)
    {
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
        SetProgress(1f, "星球着陆准备完成");
        IsComplete = true;
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        gameObject.SetActive(false);
    }
}
