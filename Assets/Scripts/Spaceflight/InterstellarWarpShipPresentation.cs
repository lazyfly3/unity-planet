using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class InterstellarWarpShipPresentation : MonoBehaviour
{
    struct RendererState
    {
        public Renderer renderer;
        public bool enabled;
    }

    readonly List<RendererState> rendererStates = new List<RendererState>(32);
    readonly List<Mesh> bakedMeshes = new List<Mesh>(4);

    GameObject proxyRoot;
    public bool IsActive => proxyRoot != null;
    public Transform PresentationTransform =>
        proxyRoot == null ? null : proxyRoot.transform;

    public bool Begin(Transform source)
    {
        Restore();
        if (source == null)
            return false;

        proxyRoot = new GameObject("InterstellarWarpShipVisualProxy");
        Scene sourceScene = source.gameObject.scene;
        if (sourceScene.IsValid())
            SceneManager.MoveGameObjectToScene(proxyRoot, sourceScene);
        proxyRoot.transform.SetPositionAndRotation(source.position, source.rotation);
        proxyRoot.transform.localScale = source.lossyScale;

        Renderer[] renderers = source.GetComponentsInChildren<Renderer>(true);
        int copiedRendererCount = 0;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            rendererStates.Add(new RendererState
            {
                renderer = renderer,
                enabled = renderer.enabled
            });

            bool visible = renderer.enabled && renderer.gameObject.activeInHierarchy;
            if (visible && TryCopyRenderer(renderer, source, proxyRoot.transform))
                copiedRendererCount++;
            if (renderer.enabled)
                renderer.enabled = false;
        }

        if (copiedRendererCount > 0)
            return true;

        Restore();
        return false;
    }

    public void SetPose(Vector3 position, Quaternion rotation)
    {
        if (proxyRoot != null)
            proxyRoot.transform.SetPositionAndRotation(position, rotation);
    }

    public void Restore()
    {
        foreach (RendererState state in rendererStates)
        {
            if (state.renderer != null)
                state.renderer.enabled = state.enabled;
        }
        rendererStates.Clear();

        if (proxyRoot != null)
            DestroyRuntimeObject(proxyRoot);
        proxyRoot = null;

        foreach (Mesh mesh in bakedMeshes)
        {
            if (mesh != null)
                DestroyRuntimeObject(mesh);
        }
        bakedMeshes.Clear();
    }

    bool TryCopyRenderer(
        Renderer sourceRenderer,
        Transform source,
        Transform proxy)
    {
        Mesh mesh;
        if (sourceRenderer is MeshRenderer)
        {
            MeshFilter filter = sourceRenderer.GetComponent<MeshFilter>();
            mesh = filter == null ? null : filter.sharedMesh;
        }
        else if (sourceRenderer is SkinnedMeshRenderer skinned)
        {
            mesh = new Mesh
            {
                name = skinned.sharedMesh == null
                    ? "WarpProxySkinnedMesh"
                    : skinned.sharedMesh.name + "_WarpProxy"
            };
            skinned.BakeMesh(mesh);
            bakedMeshes.Add(mesh);
        }
        else
        {
            return false;
        }

        if (mesh == null)
            return false;

        var copy = new GameObject(sourceRenderer.gameObject.name + "_WarpProxy");
        copy.layer = sourceRenderer.gameObject.layer;
        copy.transform.SetParent(proxy, false);
        copy.transform.localPosition = source.InverseTransformPoint(
            sourceRenderer.transform.position);
        copy.transform.localRotation =
            Quaternion.Inverse(source.rotation) * sourceRenderer.transform.rotation;
        copy.transform.localScale = DivideScale(
            sourceRenderer.transform.lossyScale,
            source.lossyScale);

        MeshFilter copyFilter = copy.AddComponent<MeshFilter>();
        copyFilter.sharedMesh = mesh;
        MeshRenderer copyRenderer = copy.AddComponent<MeshRenderer>();
        copyRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
        copyRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        copyRenderer.receiveShadows = sourceRenderer.receiveShadows;
        copyRenderer.lightProbeUsage = LightProbeUsage.Off;
        copyRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
        copyRenderer.motionVectorGenerationMode =
            MotionVectorGenerationMode.ForceNoMotion;
        return true;
    }

    static Vector3 DivideScale(Vector3 value, Vector3 divisor)
    {
        return new Vector3(
            Mathf.Abs(divisor.x) <= 0.000001f ? value.x : value.x / divisor.x,
            Mathf.Abs(divisor.y) <= 0.000001f ? value.y : value.y / divisor.y,
            Mathf.Abs(divisor.z) <= 0.000001f ? value.z : value.z / divisor.z);
    }

    static void DestroyRuntimeObject(Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            Destroy(value);
        else
            DestroyImmediate(value);
    }

    void OnDisable()
    {
        Restore();
    }
}
