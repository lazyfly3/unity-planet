using System.IO;
using UnityEditor;
using UnityEngine;

public static class CreatureLimbPrefabAssetBuilder
{
    const string Root = "Assets/Creatures/Limbs/Presets";

    [InitializeOnLoadMethod]
    static void ScheduleBuild()
    {
        EditorApplication.delayCall += BuildMissingAssets;
    }

    static void BuildMissingAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        EnsureFolder("Assets/Creatures");
        EnsureFolder("Assets/Creatures/Limbs");
        EnsureFolder(Root);
        bool changed = false;
        for (int i = 0; i < CreatureLimbCatalog.AllPresets.Count; i++)
        {
            CreatureLimbPreset preset = CreatureLimbCatalog.AllPresets[i];
            string path = Root + "/" + PresetAssetName(preset, i) + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) continue;
            CreatePrefab(preset, path);
            changed = true;
        }
        if (changed) AssetDatabase.SaveAssets();
    }

    static void CreatePrefab(CreatureLimbPreset preset, string path)
    {
        string rootName = preset.kind == CreatureLimbKind.Arm ? "Shoulder" : "Hip";
        string middleName = preset.kind == CreatureLimbKind.Arm ? "Elbow" : "Knee";
        string endName = preset.kind == CreatureLimbKind.Arm ? "Wrist" : "Ankle";
        var root = new GameObject(Path.GetFileNameWithoutExtension(path));
        Transform rootJoint = NewJoint(rootName, root.transform, Vector3.zero);
        Transform middle = NewJoint(middleName, rootJoint, preset.middle);
        Transform end = NewJoint(endName, middle, preset.end - preset.middle);
        Transform socket = NewJoint("EndSocket", end, Vector3.zero);
        var authoring = root.AddComponent<CreatureLimbPresetAuthoring>();
        authoring.presetId = preset.id;
        authoring.kind = preset.kind;
        authoring.rootJoint = rootJoint;
        authoring.middleJoint = middle;
        authoring.endJoint = end;
        authoring.endSocket = socket;
        authoring.radiusProfile = preset.radiusProfile;
        authoring.upperJointLimit = preset.upperJointLimit;
        authoring.lowerJointLimit = preset.lowerJointLimit;
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }

    static Transform NewJoint(string name, Transform parent, Vector3 localPosition)
    {
        var joint = new GameObject(name).transform;
        joint.SetParent(parent, false);
        joint.localPosition = localPosition;
        return joint;
    }

    static string PresetAssetName(CreatureLimbPreset preset, int index)
    {
        int localIndex = preset.kind == CreatureLimbKind.Arm ? index + 1 : index - 7;
        return (preset.kind == CreatureLimbKind.Arm ? "ArmPreset_" : "LegPreset_")
            + localIndex.ToString("00");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string name = Path.GetFileName(path);
        AssetDatabase.CreateFolder(parent, name);
    }
}
