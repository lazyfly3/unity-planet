using UnityEngine;

[DefaultExecutionOrder(-590)]
[DisallowMultipleComponent]
public sealed class PlayerSpacecraftWeaponInput : MonoBehaviour, ISpacecraftWeaponCommandSource
{
    [SerializeField] bool captureEnabled = true;
    [SerializeField, Range(1, 2)] int selectedGroup = 1;
    [SerializeField] bool targetLockEnabled;

    SpacecraftFireCommand command;

    public bool CaptureEnabled
    {
        get => captureEnabled;
        set
        {
            captureEnabled = value;
            if (!value)
                command = new SpacecraftFireCommand(selectedGroup, false, targetLockEnabled);
        }
    }

    public SpacecraftFireCommand FireCommand => command;
    public bool TargetLockEnabled => targetLockEnabled;

    void Update()
    {
        if (captureEnabled)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
                selectedGroup = 1;
            if (Input.GetKeyDown(KeyCode.Alpha2))
                selectedGroup = 2;
            if (Input.GetKeyDown(KeyCode.Tab))
                targetLockEnabled = !targetLockEnabled;
        }

        bool canFire = captureEnabled && Cursor.lockState == CursorLockMode.Locked;
        command = new SpacecraftFireCommand(
            selectedGroup,
            canFire && Input.GetMouseButton(0),
            targetLockEnabled);
    }
}
