using UnityEngine;

[DisallowMultipleComponent]
public sealed class SphericalGravitySource : MonoBehaviour
{
    [SerializeField, Min(0.1f)] float radius = 100f;
    [SerializeField, Min(0f)] float surfaceGravity = 9.8f;

    public Vector3 Center => transform.position;
    public float Radius => radius;
    public float SurfaceGravity => surfaceGravity;
    public float GravitationalParameter => PlanetGravity.ComputeGravitationalParameter(surfaceGravity, radius);

    public Vector3 GetUp(Vector3 worldPosition)
    {
return PlanetGravity.GetUp(worldPosition, Center);
    
}

    public Vector3 GetGravity(Vector3 worldPosition)
    {
return PlanetGravity.GetGravitationalAcceleration(worldPosition, Center, GravitationalParameter);
    
}

    public Vector3 GetSurfacePoint(Vector3 direction)
    {
Vector3 normalized = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;
        return Center + normalized * radius;
    
}

    public void Configure(float newRadius, float newSurfaceGravity)
    {
radius = Mathf.Max(0.1f, newRadius);
        surfaceGravity = Mathf.Max(0f, newSurfaceGravity);
    
}

    void OnValidate()
    {
        radius = Mathf.Max(0.1f, radius);
        surfaceGravity = Mathf.Max(0f, surfaceGravity);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.75f, 1f, 0.35f);
        Gizmos.DrawWireSphere(Center, radius);
    }
}
