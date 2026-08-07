public enum PlanetMissionEnvironmentKind
{
    Natural = 0,
    Urban = 1
}

/// <summary>
/// Chooses the battlefield independently from mission rules.  The result is
/// deterministic so revisiting the same planet mission does not reroll while
/// the player is looking at the orbit selection screen.
/// </summary>
public static class PlanetMissionEnvironmentResolver
{
    const string BossMissionId = "modular_boss";

    public static PlanetMissionEnvironmentKind Resolve(
        int worldSeed,
        string planetId,
        string missionId)
    {
        if (string.Equals(
                missionId,
                BossMissionId,
                System.StringComparison.Ordinal))
        {
            return PlanetMissionEnvironmentKind.Urban;
        }

        unchecked
        {
            uint hash = 2166136261u;
            HashInt(ref hash, worldSeed);
            HashString(ref hash, planetId);
            HashString(ref hash, missionId);

            // Avalanche the FNV state before using its low bit. This avoids
            // tying the result to the final character of a mission id.
            hash ^= hash >> 16;
            hash *= 0x85EBCA6Bu;
            hash ^= hash >> 13;
            hash *= 0xC2B2AE35u;
            hash ^= hash >> 16;
            return (hash & 1u) == 0u
                ? PlanetMissionEnvironmentKind.Natural
                : PlanetMissionEnvironmentKind.Urban;
        }
    }

    public static string GetDisplayName(
        PlanetMissionEnvironmentKind environment)
    {
        return environment == PlanetMissionEnvironmentKind.Urban
            ? "城市城区"
            : "自然地貌";
    }

    static void HashInt(ref uint hash, int value)
    {
        unchecked
        {
            hash = (hash ^ (byte)value) * 16777619u;
            hash = (hash ^ (byte)(value >> 8)) * 16777619u;
            hash = (hash ^ (byte)(value >> 16)) * 16777619u;
            hash = (hash ^ (byte)(value >> 24)) * 16777619u;
        }
    }

    static void HashString(ref uint hash, string value)
    {
        string normalized = value ?? string.Empty;
        for (int index = 0; index < normalized.Length; index++)
        {
            char character = normalized[index];
            hash = (hash ^ (byte)character) * 16777619u;
            hash = (hash ^ (byte)(character >> 8)) * 16777619u;
        }
    }
}
