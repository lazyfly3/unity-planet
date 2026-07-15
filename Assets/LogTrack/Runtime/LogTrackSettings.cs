/// <summary>
/// LogTrack 运行时全局配置。Editor 工具窗口与 Play 时自动启动共用 PlayerPrefs。
/// </summary>
public static class LogTrackSettings
{
    private const string PrefRingBufferSize = "LogTrack.RingBufferSize";
    private const string PrefAutoStartOnPlay = "LogTrack.AutoStartOnPlay";
    private const string PrefExportOnStop = "LogTrack.ExportOnStop";
    private const string PrefPdbRelativePath = "LogTrack.PdbRelativePath";
    private const string PrefInstrumentRoot = "LogTrack.InstrumentRoot";
    private const string PrefPdbOutputDir = "LogTrack.PdbOutputDir";

    public const int MinRingBufferSize = 1;
    public const int MaxRingBufferSize = 10000;
    public const int DefaultRingBufferSizeFallback = 100;
    public const string DefaultPdbRelativePath = "Assets/LogTrackGenerated/LogPdb.pdb.json";
    public const string DefaultInstrumentRoot = "Assets/Scripts";
    public const string DefaultPdbOutputDir = "Assets/LogTrackGenerated";

    public static int DefaultRingBufferSize
    {
        get => ClampRingBufferSize(UnityEngine.PlayerPrefs.GetInt(PrefRingBufferSize, DefaultRingBufferSizeFallback));
        set => UnityEngine.PlayerPrefs.SetInt(PrefRingBufferSize, ClampRingBufferSize(value));
    }

    public static bool AutoStartOnPlay
    {
        get => UnityEngine.PlayerPrefs.GetInt(PrefAutoStartOnPlay, 0) == 1;
        set => UnityEngine.PlayerPrefs.SetInt(PrefAutoStartOnPlay, value ? 1 : 0);
    }

    public static bool ExportOnStop
    {
        get => UnityEngine.PlayerPrefs.GetInt(PrefExportOnStop, 1) == 1;
        set => UnityEngine.PlayerPrefs.SetInt(PrefExportOnStop, value ? 1 : 0);
    }

    public static string PdbRelativePath
    {
        get
        {
            var path = UnityEngine.PlayerPrefs.GetString(PrefPdbRelativePath, DefaultPdbRelativePath);
            return string.IsNullOrEmpty(path) ? DefaultPdbRelativePath : path;
        }
        set => UnityEngine.PlayerPrefs.SetString(PrefPdbRelativePath, string.IsNullOrEmpty(value) ? DefaultPdbRelativePath : value);
    }

    public static string InstrumentRoot
    {
        get
        {
            var path = UnityEngine.PlayerPrefs.GetString(PrefInstrumentRoot, DefaultInstrumentRoot);
            return string.IsNullOrEmpty(path) ? DefaultInstrumentRoot : path;
        }
        set => UnityEngine.PlayerPrefs.SetString(PrefInstrumentRoot, string.IsNullOrEmpty(value) ? DefaultInstrumentRoot : value);
    }

    public static string PdbOutputDir
    {
        get
        {
            var path = UnityEngine.PlayerPrefs.GetString(PrefPdbOutputDir, DefaultPdbOutputDir);
            return string.IsNullOrEmpty(path) ? DefaultPdbOutputDir : path;
        }
        set => UnityEngine.PlayerPrefs.SetString(PrefPdbOutputDir, string.IsNullOrEmpty(value) ? DefaultPdbOutputDir : value);
    }

    public static int ClampRingBufferSize(int size)
    {
        if (size < MinRingBufferSize) return MinRingBufferSize;
        if (size > MaxRingBufferSize) return MaxRingBufferSize;
        return size;
    }
}
