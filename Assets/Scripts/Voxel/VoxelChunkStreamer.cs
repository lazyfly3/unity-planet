using UnityEngine;

/// <summary>
/// 第三阶段：驱动 VoxelWorld 按玩家位置流式加载/卸载 Chunk。
/// 挂到任意物体上，把 Player 和 VoxelWorld 拖进 Inspector 即可。
/// </summary>
public class VoxelChunkStreamer : MonoBehaviour
{
    [SerializeField] VoxelWorld voxelWorld;
    [SerializeField] Transform streamingTarget;
    [SerializeField] float updateInterval = 0.25f;

    float nextUpdateTime;

    void Awake()
    {
        if (streamingTarget == null)
            streamingTarget = transform;
    }

    void Start()
    {
        if (voxelWorld == null || streamingTarget == null)
            return;

        if (voxelWorld.UsePlanetGeneration)
            return;

        voxelWorld.UpdateStreaming(streamingTarget.position);
    }

    void Update()
    {
        if (voxelWorld == null || streamingTarget == null)
            return;

        if (voxelWorld.UsePlanetGeneration)
            return;

        if (Time.time < nextUpdateTime)
            return;

        nextUpdateTime = Time.time + updateInterval;
        voxelWorld.UpdateStreaming(streamingTarget.position);
    }
}
