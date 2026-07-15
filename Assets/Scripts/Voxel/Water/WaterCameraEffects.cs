using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class WaterCameraEffects : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] float maximumTint = 0.82f;
    [SerializeField, Min(100f)] float underwaterCutoffFrequency = 900f;
    [SerializeField, Min(0.1f)] float transitionSpeed = 4f;

    Camera targetCamera;
    AudioLowPassFilter lowPass;
    Material effectMaterial;
    float blend;

    void Awake()
    {
        targetCamera = GetComponent<Camera>();
        targetCamera.depthTextureMode |= DepthTextureMode.Depth;
        lowPass = GetComponent<AudioLowPassFilter>();
        if (lowPass == null)
            lowPass = gameObject.AddComponent<AudioLowPassFilter>();
        Shader shader = Shader.Find("Hidden/Voxel Planet/Underwater Screen");
        if (shader != null)
            effectMaterial = new Material(shader);
    }

    void Update()
    {
        bool underwater = PlanetRiverSystem.TrySampleAny(transform.position, out WaterSample water)
            && water.signedDistance < 0f;
        float target = underwater ? maximumTint : 0f;
        blend = Mathf.MoveTowards(blend, target, transitionSpeed * Time.unscaledDeltaTime);
        lowPass.cutoffFrequency = Mathf.Lerp(22000f, underwaterCutoffFrequency, blend);
        lowPass.enabled = blend > 0.001f;
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (effectMaterial == null || blend <= 0.001f)
        {
            Graphics.Blit(source, destination);
            return;
        }
        effectMaterial.SetFloat("_Blend", blend);
        Graphics.Blit(source, destination, effectMaterial);
    }

    void OnDestroy()
    {
        if (effectMaterial != null)
            Destroy(effectMaterial);
    }
}
