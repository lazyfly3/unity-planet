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

    public void Show(string initialStatus)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(109);}
    try
    {
        gameObject.SetActive(true);
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
        startedAt = Time.realtimeSinceStartup;
        displayedProgress = 0f;
        SetProgress(0.01f, initialStatus);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void SetProgress(float progress, string status)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(110, (int)progress);}
    try
    {
        progress = Mathf.Clamp01(progress);
        displayedProgress = Mathf.Max(displayedProgress, progress);
        progressFill.fillAmount = displayedProgress;
        progressText.text = Mathf.RoundToInt(displayedProgress * 100f) + "%";
        statusText.text = status ?? string.Empty;

        float elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - startedAt);
        if (displayedProgress < 0.04f || elapsed < 0.5f)
        {
            remainingText.text = "正在估算剩余时间...";
            return;
        }

        float remaining = elapsed * (1f - displayedProgress) / displayedProgress;
        remainingText.text = remaining >= 60f
            ? $"预计剩余 {Mathf.CeilToInt(remaining / 60f)} 分钟"
            : $"预计剩余 {Mathf.Max(1, Mathf.CeilToInt(remaining))} 秒";
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void Complete()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(111);}
    try
    {
        SetProgress(1f, "星球构筑完成");
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        gameObject.SetActive(false);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}
}
