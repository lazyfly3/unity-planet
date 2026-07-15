using UnityEngine;

public static class VoxelRaycastUtility
{
    const float Epsilon = 0.01f;

    public static bool TryGetTargetVoxel(Ray ray, float reach, Transform voxelWorldTransform, out Vector3Int voxelCoord, out RaycastHit hit)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(106, (int)reach);}
        if (Physics.Raycast(ray, out hit, reach))
        {
            voxelCoord = GetVoxelCoordFromHit(hit, voxelWorldTransform, dig: true);
            return true;
        }

        voxelCoord = default;
        return false;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static Vector3Int GetVoxelCoordFromHit(RaycastHit hit, Transform voxelWorldTransform, bool dig)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(107, (dig?1:0));}
        float bias = dig ? -Epsilon : Epsilon;
        Vector3 worldPoint = hit.point + hit.normal * bias;
        Vector3 localPoint = voxelWorldTransform.InverseTransformPoint(worldPoint);

        return new Vector3Int(
            Mathf.FloorToInt(localPoint.x),
            Mathf.FloorToInt(localPoint.y),
            Mathf.FloorToInt(localPoint.z)
        );
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public static Vector3 GetVoxelLocalCenter(Vector3Int voxelCoord)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(108);}
        return new Vector3(voxelCoord.x + 0.5f, voxelCoord.y + 0.5f, voxelCoord.z + 0.5f);
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}
}
