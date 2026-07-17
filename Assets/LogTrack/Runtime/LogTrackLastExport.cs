/// <summary>
/// 最近一次 Play 结束时的导出结果，供 Editor 弹窗提示。
/// </summary>
public static class LogTrackLastExport
{
    public static bool HasPending { get; private set; }
    public static bool Success { get; private set; }
    public static string TextLogPath { get; private set; }
    public static string BinLogPath { get; private set; }
    public static string ErrorMessage { get; private set; }

    public static void Clear()
    {
        HasPending = false;
        Success = false;
        TextLogPath = null;
        BinLogPath = null;
        ErrorMessage = null;
    }

    public static void RecordSuccess(string textPath, string binPath)
    {
        HasPending = true;
        Success = true;
        TextLogPath = textPath;
        BinLogPath = binPath;
        ErrorMessage = null;
    }

    public static void RecordFailure(string message)
    {
        HasPending = true;
        Success = false;
        TextLogPath = null;
        BinLogPath = null;
        ErrorMessage = message;
    }
}
