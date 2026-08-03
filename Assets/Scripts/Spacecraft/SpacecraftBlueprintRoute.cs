using System.IO;

public enum SpacecraftBlueprintRoute
{
    ModularAssembly,
    LegacyWorkshop
}

public static class SpacecraftBlueprintRouteResolver
{
    public const string ModularBlueprintFileName = "modular_ship.json";
    public const string LegacyBlueprintFileName = "ship.json";

    public static SpacecraftBlueprintRoute Resolve(
        string slotId,
        bool forceModularAssembly = false)
    {
        return Resolve(
            HasModularBlueprint(slotId),
            HasLegacyBlueprint(slotId),
            forceModularAssembly);
    }

    public static SpacecraftBlueprintRoute Resolve(
        bool hasModularBlueprint,
        bool hasLegacyBlueprint,
        bool forceModularAssembly = false)
    {
        if (forceModularAssembly || hasModularBlueprint)
            return SpacecraftBlueprintRoute.ModularAssembly;
        return hasLegacyBlueprint
            ? SpacecraftBlueprintRoute.LegacyWorkshop
            : SpacecraftBlueprintRoute.ModularAssembly;
    }

    public static bool HasModularBlueprint(string slotId)
    {
        return File.Exists(GetBlueprintPath(
            slotId,
            ModularBlueprintFileName));
    }

    public static bool HasLegacyBlueprint(string slotId)
    {
        return File.Exists(GetBlueprintPath(
            slotId,
            LegacyBlueprintFileName));
    }

    public static string GetBlueprintPath(
        string slotId,
        string fileName)
    {
        return Path.Combine(
            GalaxySaveSlotService.GetSpacecraftDirectory(slotId),
            Path.GetFileName(fileName));
    }
}
