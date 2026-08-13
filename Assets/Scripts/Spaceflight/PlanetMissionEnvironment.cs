public enum PlanetMissionEnvironmentKind
{
    // Kept only so old serialized/context data can still deserialize. Formal
    // chapter missions are canonicalized to Urban before loading.
    Natural = 0,
    Urban = 1
}

/// <summary>
/// Formal chapter missions use one battlefield contract: every mission is an
/// urban city. Natural remains an enum value only for backwards compatibility
/// with old data and is never returned by this resolver.
/// </summary>
public static class PlanetMissionEnvironmentResolver
{
    public static PlanetMissionEnvironmentKind Resolve(
        int worldSeed,
        string planetId,
        string missionId)
    {
        return PlanetMissionEnvironmentKind.Urban;
    }

    public static string GetDisplayName(
        PlanetMissionEnvironmentKind environment)
    {
        return "城市城区";
    }
}
