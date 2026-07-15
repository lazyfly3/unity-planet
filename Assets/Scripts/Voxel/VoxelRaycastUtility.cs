using UnityEngine;

public static class VoxelRaycastUtility
{
    const float Epsilon = 0.01f;

    public static bool TryGetTargetVoxel(Ray ray, float reach, Transform voxelWorldTransform, out Vector3Int voxelCoord, out RaycastHit hit)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(140, (int)reach);}
    try
    {
        if (Physics.Raycast(ray, out hit, reach))
        {
            voxelCoord = GetVoxelCoordFromHit(hit, voxelWorldTransform, dig: true);
            return true;
        }

        voxelCoord = default;
        return false;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static Vector3Int GetVoxelCoordFromHit(RaycastHit hit, Transform voxelWorldTransform, bool dig)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(141, (dig?1:0));}
    try
    {
        float bias = dig ? -Epsilon : Epsilon;
        Vector3 worldPoint = hit.point + hit.normal * bias;
        Vector3 localPoint = voxelWorldTransform.InverseTransformPoint(worldPoint);

        return new Vector3Int(
            Mathf.FloorToInt(localPoint.x),
            Mathf.FloorToInt(localPoint.y),
            Mathf.FloorToInt(localPoint.z)
        );
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static Vector3 GetVoxelLocalCenter(Vector3Int voxelCoord)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(142);}
    try
    {
        return new Vector3(voxelCoord.x + 0.5f, voxelCoord.y + 0.5f, voxelCoord.z + 0.5f);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}
}
