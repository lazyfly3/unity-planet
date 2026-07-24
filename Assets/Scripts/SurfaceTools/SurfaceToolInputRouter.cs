public static class SurfaceToolInputRouter
{
    static SurfaceToolController current;

    public static SurfaceToolController Current => current;
    public static bool CanWorldInteractionUsePrimary =>
        current == null || !current.ConsumesPrimaryAction;
    public static bool CanInventoryUseSecondary =>
        current == null || !current.ConsumesSecondaryAction;

    public static void Register(SurfaceToolController controller)
    {
        if (controller != null)
            current = controller;
    }

    public static void Unregister(SurfaceToolController controller)
    {
        if (current == controller)
            current = null;
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(
        UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        current = null;
    }
}
