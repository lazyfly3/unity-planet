using System.Collections.Generic;
using UnityEngine;

public static class DisplayResolutionSettings
{
    static readonly Vector2Int[] CommonResolutions =
    {
        new Vector2Int(1280, 720),
        new Vector2Int(1366, 768),
        new Vector2Int(1600, 900),
        new Vector2Int(1920, 1080),
        new Vector2Int(2560, 1080),
        new Vector2Int(2560, 1440),
        new Vector2Int(3440, 1440),
        new Vector2Int(3840, 2160)
    };

    public static List<Vector2Int> BuildAvailableList()
    {
        var result = new List<Vector2Int>();
        int displayWidth = Display.main != null
            ? Display.main.systemWidth
            : Screen.currentResolution.width;
        int displayHeight = Display.main != null
            ? Display.main.systemHeight
            : Screen.currentResolution.height;
        displayWidth = Mathf.Max(
            displayWidth,
            Screen.currentResolution.width,
            Screen.width);
        displayHeight = Mathf.Max(
            displayHeight,
            Screen.currentResolution.height,
            Screen.height);

        for (int index = 0; index < CommonResolutions.Length; index++)
        {
            Vector2Int candidate = CommonResolutions[index];
            if (candidate.x <= displayWidth && candidate.y <= displayHeight)
                AddUnique(result, candidate);
        }

        if (result.Count == 0)
        {
            AddIfUsable(
                result,
                new Vector2Int(Screen.width, Screen.height),
                displayWidth,
                displayHeight);
        }
        if (result.Count == 0)
        {
            AddUnique(result, new Vector2Int(1280, 720));
        }

        result.Sort((left, right) =>
        {
            long leftPixels = (long)left.x * left.y;
            long rightPixels = (long)right.x * right.y;
            int pixelOrder = leftPixels.CompareTo(rightPixels);
            return pixelOrder != 0 ? pixelOrder : left.x.CompareTo(right.x);
        });
        return result;
    }

    public static void ApplySavedSettings()
    {
        QualitySettings.vSyncCount = 0;
        PlayerPrefs.DeleteKey("VSync");

        bool fullscreen = PlayerPrefs.GetInt(
            "Fullscreen",
            Screen.fullScreen ? 1 : 0) != 0;
        List<Vector2Int> options = BuildAvailableList();
        int savedWidth = PlayerPrefs.GetInt("ResolutionWidth", Screen.width);
        int savedHeight = PlayerPrefs.GetInt("ResolutionHeight", Screen.height);
        Vector2Int selected = FindClosest(
            options,
            new Vector2Int(savedWidth, savedHeight));
        Apply(selected.x, selected.y, fullscreen);
    }

    public static void Apply(
        int width,
        int height,
        bool fullscreen,
        bool save = true)
    {
        width = Mathf.Max(1024, width);
        height = Mathf.Max(576, height);
        FullScreenMode mode = fullscreen
            ? FullScreenMode.FullScreenWindow
            : FullScreenMode.Windowed;
        QualitySettings.vSyncCount = 0;
        Screen.SetResolution(width, height, mode);

        if (!save)
            return;
        PlayerPrefs.SetInt("ResolutionWidth", width);
        PlayerPrefs.SetInt("ResolutionHeight", height);
        PlayerPrefs.SetInt("Fullscreen", fullscreen ? 1 : 0);
    }

    public static string Format(int width, int height)
    {
        return width + " × " + height;
    }

    static Vector2Int FindClosest(
        List<Vector2Int> options,
        Vector2Int requested)
    {
        if (options == null || options.Count == 0)
            return new Vector2Int(Screen.width, Screen.height);
        Vector2Int closest = options[0];
        long bestScore = long.MaxValue;
        for (int index = 0; index < options.Count; index++)
        {
            Vector2Int candidate = options[index];
            if (candidate == requested)
                return candidate;
            long widthDelta = candidate.x - requested.x;
            long heightDelta = candidate.y - requested.y;
            long score = widthDelta * widthDelta +
                         heightDelta * heightDelta;
            if (score >= bestScore)
                continue;
            closest = candidate;
            bestScore = score;
        }
        return closest;
    }

    static void AddIfUsable(
        List<Vector2Int> target,
        Vector2Int candidate,
        int displayWidth,
        int displayHeight)
    {
        if (candidate.x < 1024 || candidate.y < 576 ||
            candidate.x > displayWidth || candidate.y > displayHeight)
        {
            return;
        }
        AddUnique(target, candidate);
    }

    static void AddUnique(List<Vector2Int> target, Vector2Int candidate)
    {
        if (!target.Contains(candidate))
            target.Add(candidate);
    }
}
