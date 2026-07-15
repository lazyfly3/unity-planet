#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class PortalPrefabCreator
{
    const string PrefabPath = "Assets/Prefabs/Portal.prefab";
    const string RenderTexturePath = "Assets/RenderTextures/PortalRT.renderTexture";
    const string MaterialPath = "Assets/Materials/PortalSurface.mat";
    const string FrameMaterialPath = "Assets/Materials/PortalFrame.mat";

    [InitializeOnLoadMethod]
    static void AutoCreateOnLoad()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                CreatePortalPrefab();
        };
    }

    [MenuItem("Assets/Create/Portal/Portal Prefab")]
    static void CreateFromMenu()
    {
        CreatePortalPrefab();
    }

    [MenuItem("GameObject/Portal/Create Portal Prefab", false, 10)]
    static void CreateFromGameObjectMenu()
    {
        CreatePortalPrefab();
    }

    public static GameObject CreatePortalPrefab()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(51);}
    try
    {
        EnsureFolders();
        EnsureRenderTexture();
        EnsureMaterials();

        RenderTexture rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexturePath);
        Material surfaceMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Material frameMat = AssetDatabase.LoadAssetAtPath<Material>(FrameMaterialPath);

        GameObject root = BuildPortalHierarchy(rt, surfaceMat, frameMat);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"传送门预制体已生成：{PrefabPath}");
        return prefab;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
    }

    static void EnsureRenderTexture()
    {
        if (AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexturePath) != null)
            return;

        RenderTexture rt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32)
        {
            name = "PortalRT"
        };
        rt.Create();
        AssetDatabase.CreateAsset(rt, RenderTexturePath);
    }

    static void EnsureMaterials()
    {
        RenderTexture rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexturePath);

        Material surface = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (surface == null)
        {
            surface = new Material(Shader.Find("Unlit/Texture"))
            {
                name = "PortalSurface",
                mainTexture = rt
            };
            AssetDatabase.CreateAsset(surface, MaterialPath);
        }
        else if (rt != null)
        {
            surface.mainTexture = rt;
            EditorUtility.SetDirty(surface);
        }

        Material frame = AssetDatabase.LoadAssetAtPath<Material>(FrameMaterialPath);
        if (frame == null)
        {
            frame = new Material(Shader.Find("Standard"))
            {
                name = "PortalFrame",
                color = new Color(0.15f, 0.15f, 0.18f)
            };
            AssetDatabase.CreateAsset(frame, FrameMaterialPath);
        }
    }

    static GameObject BuildPortalHierarchy(RenderTexture rt, Material surfaceMat, Material frameMat)
    {
        GameObject root = new GameObject("Portal");
        Portal portal = root.AddComponent<Portal>();

        BoxCollider trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(2f, 3f, 0.5f);
        trigger.center = Vector3.zero;

        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frame.name = "Frame";
        frame.transform.SetParent(root.transform, false);
        frame.transform.localScale = new Vector3(2.2f, 3.2f, 0.15f);
        Object.DestroyImmediate(frame.GetComponent<BoxCollider>());
        if (frameMat != null)
            frame.GetComponent<Renderer>().sharedMaterial = frameMat;

        GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
        surface.name = "PortalSurface";
        surface.transform.SetParent(root.transform, false);
        surface.transform.localPosition = new Vector3(0f, 0f, -0.08f);
        surface.transform.localRotation = Quaternion.identity;
        surface.transform.localScale = new Vector3(2f, 3f, 1f);
        Object.DestroyImmediate(surface.GetComponent<Collider>());
        if (surfaceMat != null)
            surface.GetComponent<Renderer>().sharedMaterial = surfaceMat;

        GameObject cameraObject = new GameObject("PortalCamera");
        cameraObject.transform.SetParent(root.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 0f, -0.5f);

        Camera portalCamera = cameraObject.AddComponent<Camera>();
        portalCamera.enabled = false;
        portalCamera.targetTexture = rt;
        portalCamera.clearFlags = CameraClearFlags.SolidColor;
        portalCamera.backgroundColor = Color.black;
        portalCamera.depth = 10;
        portalCamera.nearClipPlane = 0.01f;
        portalCamera.stereoTargetEye = StereoTargetEyeMask.None;

        PortalCameraController cameraController = cameraObject.AddComponent<PortalCameraController>();
        SerializedObject so = new SerializedObject(cameraController);
        so.FindProperty("portal").objectReferenceValue = portal;
        so.FindProperty("renderTexture").objectReferenceValue = rt;
        so.FindProperty("useObliqueClipping").boolValue = true;
        so.FindProperty("maxRecursion").intValue = 1;
        so.ApplyModifiedPropertiesWithoutUndo();

        return root;
    }
}
#endif
