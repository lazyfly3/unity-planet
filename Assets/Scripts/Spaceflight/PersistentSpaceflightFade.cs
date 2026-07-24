using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class PersistentSpaceflightFade : MonoBehaviour
{
    static PersistentSpaceflightFade instance;

    CanvasGroup canvasGroup;
    float pendingFadeInDuration;
    float pendingSurfaceReadyTimeout;
    bool waitingForScene;
    bool pendingWaitForSurfaceReady;

    public static PersistentSpaceflightFade Instance
    {
        get
        {
            if (instance != null)
                return instance;
            var root = new GameObject("PersistentSpaceflightFade");
            instance = root.AddComponent<PersistentSpaceflightFade>();
            return instance;
        }
    }

    public float Alpha => canvasGroup == null ? 0f : canvasGroup.alpha;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        BuildCanvas();
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    public void SetBlackout(float alpha)
    {
        BuildCanvas();
        canvasGroup.alpha = Mathf.Clamp01(alpha);
        canvasGroup.blocksRaycasts = canvasGroup.alpha > 0.001f;
        canvasGroup.interactable = canvasGroup.blocksRaycasts;
    }

    public void FadeInAfterNextScene(float duration)
    {
        FadeInAfterNextScene(duration, false);
    }

    public void FadeInAfterNextScene(
        float duration,
        bool waitForSurfaceReady,
        float surfaceReadyTimeout = 12f)
    {
        pendingFadeInDuration = Mathf.Max(0f, duration);
        pendingWaitForSurfaceReady = waitForSurfaceReady;
        pendingSurfaceReadyTimeout = Mathf.Max(0.5f, surfaceReadyTimeout);
        waitingForScene = true;
        SetBlackout(1f);
    }

    public void CancelPendingTransition()
    {
        waitingForScene = false;
        pendingFadeInDuration = 0f;
        pendingWaitForSurfaceReady = false;
        pendingSurfaceReadyTimeout = 0f;
        StopAllCoroutines();
        SetBlackout(0f);
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!waitingForScene)
            return;
        waitingForScene = false;
        StopAllCoroutines();
        StartCoroutine(FadeInAfterSceneReadyRoutine(
            scene,
            pendingFadeInDuration,
            pendingWaitForSurfaceReady,
            pendingSurfaceReadyTimeout));
        pendingWaitForSurfaceReady = false;
        pendingSurfaceReadyTimeout = 0f;
    }

    IEnumerator FadeInAfterSceneReadyRoutine(
        Scene loadedScene,
        float duration,
        bool waitForSurfaceReady,
        float surfaceReadyTimeout)
    {
        yield return null;

        if (waitForSurfaceReady)
        {
            float startedAt = Time.realtimeSinceStartup;
            PlanetLoadingUI loadingUI = null;
            while (Time.realtimeSinceStartup - startedAt < surfaceReadyTimeout)
            {
                if (loadingUI == null)
                    loadingUI = FindLoadingUI(loadedScene);
                if (loadingUI != null && loadingUI.IsVisible)
                    break;
                yield return null;
            }

            if (loadingUI == null || !loadingUI.IsVisible)
            {
                Debug.LogWarning(
                    "PersistentSpaceflightFade: timed out waiting for PlanetLoadingUI " +
                    "to take over the surface transition.",
                    this);
            }

            duration = Mathf.Min(Mathf.Max(0f, duration), 0.3f);
        }

        yield return FadeInRoutine(duration);
    }

    static PlanetLoadingUI FindLoadingUI(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return null;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            PlanetLoadingUI loadingUI =
                roots[i].GetComponentInChildren<PlanetLoadingUI>(true);
            if (loadingUI != null)
                return loadingUI;
        }
        return null;
    }

    IEnumerator FadeInRoutine(float duration)
    {
        yield return null;
        if (duration <= 0f)
        {
            SetBlackout(0f);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetBlackout(1f - Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        SetBlackout(0f);
    }

    void BuildCanvas()
    {
        if (canvasGroup != null)
            return;

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        canvasGroup = gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        var blackout = new GameObject("Blackout");
        blackout.transform.SetParent(transform, false);
        RectTransform rect = blackout.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Image image = blackout.AddComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (instance == this)
            instance = null;
    }
}
