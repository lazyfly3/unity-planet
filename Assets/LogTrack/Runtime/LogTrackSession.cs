using UnityEngine;

/// <summary>
/// 可选运行时引导：挂到场景空物体上即可开始记录 LogTrack。
/// 使用前需先通过 Tools/LogTrack 对业务脚本插桩。
/// </summary>
public class LogTrackSession : MonoBehaviour
{
    [SerializeField] private int ringBufferSize = 100;
    [SerializeField] private bool exportOnDestroy = true;
    [SerializeField] private string pdbRelativePath = "Assets/LogTrackGenerated/LogPdb.pdb.json";

    private int m_frameIndex;

    private void Start()
    {
        FSPDebuger.TrackBufferSize = ringBufferSize;
        FSPDebuger.BeginTrack(ringBufferSize);
        Debug.Log($"LogTrack 已启动，RingBuffer={ringBufferSize}");
    }

    private void Update()
    {
        m_frameIndex++;
        FSPDebuger.EnterTrackFrame(m_frameIndex);
    }

    private void OnDestroy()
    {
        if (!exportOnDestroy || !FSPDebuger.EnableLogTrackInternal)
        {
            return;
        }

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
        var binPath = FSPDebuger.SaveTrack();
        var textPath = FSPDebuger.SaveTrackAsText(pdbRelativePath);
        Debug.Log($"LogTrack 手动导出: bin={binPath}, log={textPath}");
    }
}
