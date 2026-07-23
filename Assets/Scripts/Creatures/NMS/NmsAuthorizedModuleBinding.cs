using UnityEngine;

[DisallowMultipleComponent]
public sealed class NmsAuthorizedModuleBinding : MonoBehaviour
{
    [SerializeField] string moduleId;
    [SerializeField] string rootBoneName;
    [SerializeField] string[] boneNames = new string[0];

    public string ModuleId => moduleId;
    public string RootBoneName => rootBoneName;
    public string[] BoneNames => boneNames;
}
