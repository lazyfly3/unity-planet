using UnityEngine;

/// <summary>
/// 可选手动引导：当关闭「Play 时自动启动」时使用，或需要 per-scene 独立配置时使用。
/// 场景里已有 LogTrackSession 时，自动启动会被跳过。
/// </summary>
public class LogTrackSession : MonoBehaviour
{
    [SerializeField]
    [Tooltip("保留最近多少帧的 LogTrack 记录。可在 Tools/LogTrack 工具窗口设置项目默认值。")]
    private int ringBufferSize = LogTrackSettings.DefaultRingBufferSizeFallback;

    [SerializeField] private bool exportOnDestroy = true;
    [SerializeField] private string pdbRelativePath = LogTrackSettings.DefaultPdbRelativePath;

    private int m_frameIndex;
    private bool m_exported;
    private bool m_sessionStarted;

    public int RingBufferSize => ringBufferSize;

    private void Reset()
    {
        ringBufferSize = LogTrackSettings.DefaultRingBufferSize;
    }

    private void OnValidate()
    {
        ringBufferSize = LogTrackSettings.ClampRingBufferSize(ringBufferSize);
    }

    private void Start()
    {
        ringBufferSize = LogTrackSettings.ClampRingBufferSize(ringBufferSize);
        FSPDebuger.TrackBufferSize = ringBufferSize;
        FSPDebuger.BeginTrack(ringBufferSize);
        m_sessionStarted = true;
        Application.quitting += ExportIfNeeded;
        Debug.Log($"LogTrack 已启动（LogTrackSession），RingBuffer={ringBufferSize}");
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
        if (m_exported || !exportOnDestroy || !m_sessionStarted)
        {
            return;
        }

        m_exported = true;
        LogTrackRecordingExport.ExportIfNeeded();
    }

    [ContextMenu("Export LogTrack Now")]
    private void ExportNow()
    {
        m_exported = false;
        ExportIfNeeded();
    }
}
