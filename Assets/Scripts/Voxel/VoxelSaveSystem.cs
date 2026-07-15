using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class VoxelSaveSystem : MonoBehaviour
{
    [SerializeField] VoxelWorld voxelWorld;
    [SerializeField] KeyCode saveKey = KeyCode.F5;
    [SerializeField] KeyCode loadKey = KeyCode.F9;

    string SavePath => Path.Combine(Application.persistentDataPath, "voxel_world_save.json");

    void Update()
    {
        if (Input.GetKeyDown(saveKey))
            Save();

        if (Input.GetKeyDown(loadKey))
            Load();
    }

    void OnApplicationQuit()
    {
        Save();
    }

    public void Save()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(143);}
    try
    {
        if (voxelWorld == null)
            return;

        List<ChunkSaveEntry> entries = voxelWorld.GetModifiedChunkSnapshots();
        VoxelWorldSaveData data = new VoxelWorldSaveData
        {
            seed = voxelWorld.Seed,
            chunks = entries.ToArray()
        };

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(SavePath, json);
        Debug.Log($"体素存档已保存：{SavePath}（{entries.Count} 个 Chunk）");
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void Load()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(144);}
    try
    {
        if (voxelWorld == null || !File.Exists(SavePath))
            return;

        string json = File.ReadAllText(SavePath);
        VoxelWorldSaveData data = JsonUtility.FromJson<VoxelWorldSaveData>(json);
        voxelWorld.ApplySaveData(data);

        if (voxelWorld.UsePlanetGeneration)
            voxelWorld.GenerateEntirePlanet();

        Debug.Log($"体素存档已加载：{SavePath}");
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public bool HasSaveFile()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(145);}
    try
    {
        return File.Exists(SavePath);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}
}

[System.Serializable]
public class VoxelWorldSaveData
{
    public int seed;
    public ChunkSaveEntry[] chunks;
}

[System.Serializable]
public class ChunkSaveEntry
{
    public int x;
    public int y;
    public int z;
    public string base64;
}
