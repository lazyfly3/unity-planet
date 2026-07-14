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
        if (m_exported || !exportOnDestroy || !FSPDebuger.EnableLogTrackInternal)
        {
            return;
        }

        m_exported = true;

        var binPath = FSPDebuger.SaveTrack();
        if (!string.IsNullOrEmpty(binPath))
        {
            Debug.Log("LogTrack 二进制日志已导出: " + binPath);
        }

        var textPath = FSPDebuger.SaveTrackAsText(pdbRelativePath);
        if (!string.IsNullOrEmpty(textPath))
        {
            Debug.Log("LogTrack 文本日志已导出: " + textPath);
        }
    }

    [ContextMenu("Export LogTrack Now")]
    private void ExportNow()
    {
        m_exported = false;
        ExportIfNeeded();
    }
}
