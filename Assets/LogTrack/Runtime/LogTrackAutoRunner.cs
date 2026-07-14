using UnityEngine;

/// <summary>
/// Play 时由 LogTrackRuntimeBootstrap 自动创建，无需手动挂组件。
/// </summary>
internal sealed class LogTrackAutoRunner : MonoBehaviour
{
    private int m_frameIndex;
    private bool m_exported;

    private void Awake()
    {
        var ringBuffer = LogTrackSettings.DefaultRingBufferSize;
        FSPDebuger.TrackBufferSize = ringBuffer;
        FSPDebuger.BeginTrack(ringBuffer);
        Application.quitting += ExportIfNeeded;
        Debug.Log($"LogTrack 自动启动，RingBuffer={ringBuffer}");
    }

    private void Update()
    {
        m_frameIndex++;
        FSPDebuger.EnterTrackFrame(m_frameIndex);
    }

    private void OnDestroy()
    {
        Application.quitting -= ExportIfNeeded;
        ExportIfNeeded();
    }

    private void ExportIfNeeded()
    {
        if (m_exported || !LogTrackSettings.ExportOnStop || !FSPDebuger.EnableLogTrackInternal)
        {
            return;
        }

        m_exported = true;

        var binPath = FSPDebuger.SaveTrack();
        if (!string.IsNullOrEmpty(binPath))
        {
            Debug.Log("LogTrack 二进制日志已导出: " + binPath);
        }

        var textPath = FSPDebuger.SaveTrackAsText(LogTrackSettings.PdbRelativePath);
        if (!string.IsNullOrEmpty(textPath))
        {
            Debug.Log("LogTrack 文本日志已导出: " + textPath);
        }
    }
}
